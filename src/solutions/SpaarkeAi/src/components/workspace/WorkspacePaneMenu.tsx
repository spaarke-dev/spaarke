/**
 * WorkspacePaneMenu.tsx — Dropdown menu rendered in the WorkspacePane PaneHeader
 * rightSlot. A Fluent v9 Menu surface that unifies workspace-level actions in
 * one trigger (FR-12).
 *
 * Sections (post task 098 cleanup):
 *
 *   1. Select Workspace — list of workspace layouts fetched from the BFF via
 *                `useWorkspaceLayouts` (the SpaarkeAi adaptation of the WORKING
 *                LegalWorkspace hook). Each row shows a clickable pin
 *                indicator on the LEFT (task 098 — operator feedback
 *                2026-05-22) that toggles localStorage
 *                `spaarke:workspace:pinned-list` via `pinnedWorkspaces.ts`;
 *                pinned workspaces auto-open as tabs on cold load via the
 *                existing WorkspacePane mount effect (unchanged since task
 *                092). Clicking the row body (anywhere but the pin) dispatches
 *                `widget_load` so the chosen workspace opens as a new tab via
 *                the existing WorkspacePane → WorkspaceTabManager →
 *                resolveWorkspaceWidget pipeline.
 *   2. Actions  — `+ New Workspace` launches the WorkspaceLayoutWizard with
 *                the SpaarkeAi 6-template filter (FR-14); `Manage workspaces`
 *                is a stub for task 093 (side pane). Edit / Delete actions on
 *                the active workspace will live in that Manage workspaces
 *                side pane (operator: dropdown is for SELECTION, not editing).
 *
 * Removed in task 089 (operator UX feedback 2026-05-21):
 *   - "Open" section (redundant with the visible tab bar above the dropdown).
 *   - "Home" section (Home is no longer a distinct concept; Daily Briefing is
 *     just one widget rather than a special "home" tab anymore).
 *
 * Removed in task 098 (operator UX feedback 2026-05-22):
 *   - "Edit current workspace" menu entry — moved to the Manage workspaces
 *     side pane (task 093). Dropdown trigger label "Workspace" → "Workspaces".
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
  mergeClasses,
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
  AddRegular,
  SettingsRegular,
  CheckmarkRegular,
  PinRegular,
  PinFilled,
} from "@fluentui/react-icons";
import { useAiSession, useDispatchPaneEvent } from "@spaarke/ai-widgets";
import type { WorkspaceTab } from "./WorkspaceTabManager";
// useWorkspaceLayouts — SpaarkeAi adaptation of LegalWorkspace's working hook.
// See ../../hooks/useWorkspaceLayouts.ts header for the reuse rationale.
import { useWorkspaceLayouts } from "../../hooks/useWorkspaceLayouts";
// pinnedWorkspaces — task 092 localStorage contract. Task 098 surfaces the
// toggle UI here (in the dropdown) since the per-tab pin button was removed.
import {
  getPinnedWorkspaces,
  isPinned,
  pinWorkspace,
  unpinWorkspace,
} from "../../services/pinnedWorkspaces";

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
   * Retained for API back-compat after task 089 removed the Open/Home menu
   * sections; the menu no longer renders per-tab rows (the tab bar above the
   * dropdown is the canonical surface).
   */
  tabs: readonly WorkspaceTab[];
  /** The currently-active tab id, or `null` if no tab is active. Retained for back-compat (see `tabs`). */
  activeTabId: string | null;
  /**
   * Retained for API back-compat with `WorkspacePane.tsx` (task 089 removed
   * the per-tab menu entries that used this callback).
   */
  onTabSelect?: (tabId: string) => void;
  /**
   * Retained for API back-compat with `WorkspacePane.tsx` (task 089 removed
   * the per-tab `×` affordance from the menu).
   */
  onTabClose?: (tabId: string) => void;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  trigger: {
    minWidth: "auto",
  },
  tabLabel: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    flex: "1 1 auto",
    minWidth: 0,
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

  // Task 098 — pin column in the Select Workspace section. The pin icon
  // appears on the LEFT of the layout name (BEFORE the active-checkmark
  // marker) and is a clickable affordance independent of the row's main
  // click handler. The button is intentionally small (16×16 with a tiny
  // icon) so it sits comfortably inside a MenuItem row without inflating
  // the row height.
  layoutRow: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    width: "100%",
    minWidth: 0,
  },
  pinButton: {
    minWidth: "unset",
    height: "20px",
    width: "20px",
    padding: "0",
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    ":hover": {
      color: tokens.colorNeutralForeground1,
      backgroundColor: tokens.colorNeutralBackground3Hover,
    },
  },
  pinButtonActive: {
    color: tokens.colorBrandForeground1,
    ":hover": {
      color: tokens.colorBrandForeground1,
    },
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
  // tabs / activeTabId / onTabSelect / onTabClose retained for API back-compat
  // (see WorkspacePaneMenuProps JSDoc). Task 089 removed the Open + Home menu
  // sections that used these props.
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
  // Pin state — task 098 (2026-05-22)
  //
  // The localStorage-backed pin list is the source of truth (see
  // `services/pinnedWorkspaces.ts`). We hold a derived `Set<layoutId>` in
  // React state so the pin icon re-renders synchronously when the user
  // toggles it (otherwise the icon would only flip after the next external
  // re-render of WorkspacePaneMenu).
  //
  // Re-syncs when the menu opens so a freshly-pinned workspace from another
  // surface (none today, but defensive) is reflected without remounting.
  // -------------------------------------------------------------------------

  const [pinnedIds, setPinnedIds] = React.useState<Set<string>>(() => {
    const set = new Set<string>();
    for (const p of getPinnedWorkspaces()) set.add(p.layoutId);
    return set;
  });

  React.useEffect(() => {
    if (!menuOpen) return;
    const next = new Set<string>();
    for (const p of getPinnedWorkspaces()) next.add(p.layoutId);
    setPinnedIds(next);
  }, [menuOpen]);

  /**
   * Toggle the pin state for a workspace layout. Stops event propagation so
   * the surrounding MenuItem's onClick (which calls handleLayoutSelect) does
   * NOT also fire — clicking the pin icon must only flip pin state, not
   * open the workspace. Persists to localStorage via the shared
   * `pinnedWorkspaces.ts` contract; cold-load auto-open in WorkspacePane
   * consumes the same list (unchanged since task 092).
   */
  const handlePinToggle = React.useCallback(
    (
      e: React.MouseEvent | React.KeyboardEvent,
      layoutId: string,
      layoutName: string,
    ): void => {
      e.stopPropagation();
      if (!layoutId) return;
      if (pinnedIds.has(layoutId)) {
        unpinWorkspace(layoutId);
        setPinnedIds((prev) => {
          const next = new Set(prev);
          next.delete(layoutId);
          return next;
        });
      } else {
        pinWorkspace(layoutId, layoutName);
        setPinnedIds((prev) => {
          const next = new Set(prev);
          next.add(layoutId);
          return next;
        });
      }
    },
    [pinnedIds],
  );

  // -------------------------------------------------------------------------
  // Handlers
  // -------------------------------------------------------------------------

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
      // Round 4 Fix 4.1 (2026-05-21): include `displayName` at the top level
      // of the event so `WorkspacePane.tsx`'s `widget_load` handler picks it
      // up as the per-instance tab title (e.g. "Ralph Workspace 4") instead
      // of the generic registry metadata label ("Workspace"). The pane's
      // displayName-precedence ladder is: event.displayName → registry
      // metadata.displayName → widgetType. We supply the highest priority.
      dispatch("workspace", {
        type: "widget_load",
        widgetType: "workspace",
        widgetData: { layoutId, layoutName },
        displayName: layoutName,
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

  /**
   * Task 089 stub — placeholder handler for the "Manage workspaces" menu entry.
   * Task 093 will implement the side pane that this entry opens. The console
   * log is intentional so the operator can verify the menu entry routes
   * through this handler in dev tools.
   */
  const handleManageWorkspaces = React.useCallback(() => {
    setMenuOpen(false);
    console.log(
      "Manage workspaces — task 093 will implement side pane",
    );
  }, []);

  // Task 098 (2026-05-22): `handleEditWorkspace` (and its MenuItem) was
  // removed from this dropdown. Edit + Delete on the active workspace will
  // live in the Manage workspaces side pane (task 093, Wave 3). Operator
  // feedback: the dropdown is for SELECTION, not editing. The wizard launch
  // helper `launchWizard` (above) remains for "+ New Workspace"; the
  // task-093 surface will re-import + re-use it for edit/saveAs flows.

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
            Workspaces
          </Button>
        </Tooltip>
      </MenuTrigger>

      <MenuPopover data-testid="workspace-pane-menu-popover">
        <MenuList>
          {/* ───────────────────────────────────────────────────────────────
           * Section 1 — Select Workspace (all layouts).
           *
           * Task 089 (2026-05-21) renamed the section from "Switch Workspace"
           * (operator wording change) and removed the upstream "Open" / "Home"
           * sections — the open tab bar above the dropdown is the canonical
           * surface for tab interaction, and "Home" is no longer a distinct
           * concept (Daily Briefing is just one widget).
           * ─────────────────────────────────────────────────────────────── */}
          <MenuGroupHeader>Select Workspace</MenuGroupHeader>
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
              const layoutIsPinned = pinnedIds.has(layout.id);
              const pinTooltip = layoutIsPinned
                ? `Unpin ${layout.name} from start`
                : `Pin ${layout.name} to start`;

              // Pin affordance — task 098. The icon sits to the LEFT of the
              // layout name (BEFORE the active checkmark). Clicking it ONLY
              // toggles pin state (stopPropagation in the handler) so the
              // surrounding MenuItem.onClick (handleLayoutSelect) does not
              // also fire. aria-pressed reflects the current pin state for
              // assistive tech; keyboard activation (Enter/Space) routes
              // through Button's native handling.
              const pinSlot = (
                <Tooltip
                  content={pinTooltip}
                  relationship="label"
                  positioning="before"
                >
                  <Button
                    className={mergeClasses(
                      styles.pinButton,
                      layoutIsPinned && styles.pinButtonActive,
                    )}
                    appearance="subtle"
                    size="small"
                    icon={
                      layoutIsPinned ? (
                        <PinFilled />
                      ) : (
                        <PinRegular />
                      )
                    }
                    aria-label={pinTooltip}
                    aria-pressed={layoutIsPinned}
                    data-testid={`pin-workspace-${layout.id}`}
                    onClick={(e) =>
                      handlePinToggle(e, layout.id, layout.name)
                    }
                    onKeyDown={(e) => {
                      // Prevent the MenuItem from consuming Enter/Space and
                      // firing handleLayoutSelect. Button still handles the
                      // activation natively via its own click on keyup, but
                      // we stop the event from bubbling to the MenuItem.
                      if (e.key === "Enter" || e.key === " ") {
                        e.stopPropagation();
                      }
                    }}
                  />
                </Tooltip>
              );

              return (
                <MenuItem
                  key={layout.id}
                  onClick={() => handleLayoutSelect(layout.id)}
                  data-testid={`select-workspace-${layout.id}`}
                >
                  <div className={styles.layoutRow}>
                    {pinSlot}
                    <span className={styles.tabLabel}>{layout.name}</span>
                    {isActive && (
                      <CheckmarkRegular className={styles.activeMarker} />
                    )}
                  </div>
                </MenuItem>
              );
            })
          )}

          <MenuDivider />

          {/* ───────────────────────────────────────────────────────────────
           * Section 2 — Workspace actions.
           *
           *   • + New Workspace      — launches WorkspaceLayoutWizard (create).
           *   • Manage workspaces    — stub for task 093 (side pane).
           *
           * Edit + Delete moved to Manage workspaces side pane (task 093).
           * Operator feedback 2026-05-22: dropdown should be for SELECTION,
           * not editing — editing lives in the manage surface.
           * ─────────────────────────────────────────────────────────────── */}
          <MenuItem
            onClick={handleCreateWorkspace}
            data-testid="new-workspace-action"
            icon={<AddRegular className={styles.newWorkspace} />}
          >
            <span className={styles.newWorkspace}>+ New Workspace</span>
          </MenuItem>

          <MenuItem
            onClick={handleManageWorkspaces}
            data-testid="manage-workspaces-action"
            icon={<SettingsRegular />}
          >
            Manage workspaces
          </MenuItem>
        </MenuList>
      </MenuPopover>
    </Menu>
  );
};

WorkspacePaneMenu.displayName = "WorkspacePaneMenu";
