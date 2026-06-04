/**
 * ManageWorkspacesPane.tsx — Fluent v9 OverlayDrawer right-side panel listing
 * the user's workspace layouts with per-row management actions.
 *
 * # Why this file exists (history)
 *
 * Initially landed in task 093 (Wave 3, 2026-05-22) as a "view manager" style
 * pane with always-visible pin star + inline Edit + Delete icons. Task 104
 * (Wave-3-R7, 2026-05-22) reworks the body to mirror the model-driven app
 * "manage view" UX per Round 7 operator feedback:
 *
 *   > "The Manage workspace should follow the same UI/UX as the
 *   > model-driven app 'manage view'. The 'hide' is a hover-over (for us
 *   > this is 'pin'); other manage functions are in the three-dot '...'
 *   > menu drop down (pin, set as default, delete, edit); also up arrow
 *   > and down arrow to reorder the pinned workspaces — this is the order
 *   > as used in the workspace when it loads."
 *   > "If a workspace is pinned then the pin will show (not just on
 *   > hover); user needs to see the pin and then can unpin it."
 *   > "Put the pinned at the top in the current order (do not need
 *   > numbers — the default will be first, and then the order as
 *   > pin/saved/persisted)."
 *   > "The Manage workspace does not have a 'Save' button or 'Close'
 *   > button."
 *
 * # Row UX (task 104 — MDA "manage view" pattern)
 *
 * Each row is laid out as:
 *
 *   [pin icon]  Workspace name (+ "Default" badge if applicable)   [⋯ menu]
 *               System layout — Save As to edit
 *
 *   - PIN ICON VISIBILITY:
 *       Unpinned + not hovered → `opacity: 0` (still occupies space so the
 *         layout doesn't shift on hover).
 *       Unpinned + row hovered → `opacity: 1`, `PinRegular` outline at
 *         `colorNeutralForeground3`.
 *       Pinned (any hover state) → `opacity: 1`, `PinFilled` solid at
 *         `colorBrandForeground1`. Operator: "the user needs to SEE the
 *         pin and then can unpin it."
 *     Clicking the pin toggles state via `pinWorkspace`/`unpinWorkspace`
 *     (same storage as the dropdown — `pinnedWorkspaces.ts`).
 *
 *   - DEFAULT INDICATOR: the FIRST workspace in the pinned list is the
 *     "default" (opens first on cold load — `WorkspacePane.tsx` dispatches
 *     `widget_load` events in array order). We render a small Fluent v9
 *     `<Badge>` to the right of the workspace name reading "Default".
 *     Chosen over a left-side star icon because the pin column already
 *     conveys "pinned" — a second left-side icon would compete visually.
 *     The badge sits inline with the name so it scans naturally at a
 *     glance.
 *
 *   - INLINE RENAME: double-click name → Input (preserved from task 093).
 *     System layouts: double-click is a no-op.
 *
 *   - ROW CLICK (outside pin + ⋯ menu): dispatches `widget_load` so the
 *     workspace opens as a new tab via the existing
 *     `WorkspacePane → WorkspaceTabManager → resolveWorkspaceWidget`
 *     pipeline. Mirrors `WorkspacePaneMenu.handleLayoutSelect` (task 102).
 *     Closes the drawer after dispatch.
 *
 *   - THREE-DOT MENU (⋯): right side of row. Fluent v9 `<Menu>`:
 *       1. Pin / Unpin                   — label toggles based on state
 *       2. Set as default                — moves to index 0 of pinned list;
 *                                          disabled if already default
 *       3. Move up                       — swap with N-1; disabled if not
 *                                          pinned or already at top
 *       4. Move down                     — swap with N+1; disabled if not
 *                                          pinned or already at bottom
 *       5. Edit                          — launches sprk_workspacelayoutwizard
 *                                          (saveAs for system, edit for user)
 *       6. Delete                        — opens confirmation dialog; disabled
 *                                          for system layouts with tooltip
 *
 * # "Default = first in pinned list" semantics
 *
 * Task 104 deliberately does NOT introduce a new
 * `localStorage["spaarke:workspace:default-id"]` key. Instead the pinned
 * list (ordered JSON array in `localStorage["spaarke:workspace:pinned-list"]`)
 * IS the source of truth: index 0 = default. This:
 *
 *   - Reuses existing storage (zero new keys).
 *   - Reuses existing auto-open effect (`WorkspacePane.tsx` lines ~400-457)
 *     which already iterates `getPinnedWorkspaces()` in array order — so
 *     "default opens first, then pinned in user-ordered sequence" is
 *     automatic.
 *   - Reuses existing dropdown ordering (`WorkspacePaneMenu.orderedLayouts`
 *     task 102) which already puts `activeLayout` first, then
 *     `getPinnedWorkspaces()` order. The dropdown will need a small follow-up
 *     to align with task 104's "first-of-pinned-list = default" model, but
 *     since task 102's `activeLayout` typically equals the BFF default (which
 *     is itself the first pinned in most cases for new users) the visual is
 *     coherent today.
 *
 * # Footer (task 104 operator feedback: "no Save or Close button")
 *
 * Right-justified `Cancel` (secondary) + `Save` (primary) buttons inside a
 * `DrawerFooter`-style div. Both close the drawer. All operations in this
 * pane are COMMITTED INSTANTLY (pin / unpin / set-default / move / rename /
 * delete) so neither button reverts or "applies" anything — they are
 * visual exit affordances the operator expects. If we later add a
 * transactional "pending changes" mode, Cancel will revert and Save will
 * commit. The `<DrawerHeader>` ✕ button stays as a third dismissal path
 * (matches MDA behavior).
 *
 * # Section headers + ordering
 *
 *   - Pinned section: header "Pinned" (semibold base200). Rows in
 *     `getPinnedWorkspaces()` order. First row = default (carries badge).
 *   - Unpinned section: header "All workspaces" (semibold base200). Rows in
 *     BFF return order.
 *   - Divider rule between sections (1px solid colorNeutralStroke2).
 *   - If pinned list is empty: no headers, no divider — flat list of all
 *     workspaces in BFF order.
 *
 * # BFF integration
 *
 * Reuses `useAiSession()` for `authenticatedFetch` + `bffBaseUrl` (ADR-028).
 * All four endpoints PRE-EXIST in `WorkspaceLayoutEndpoints.cs` (PUT for
 * rename, DELETE for delete, GET endpoints the hook uses). NO new BFF
 * endpoints — CLAUDE.md §10 BFF Hygiene respected.
 *
 * # Standards
 *
 *   - ADR-012: SpaarkeAi-local component (depends on solution-local hook,
 *     wizard launch helper, and pin service — not reusable as-is in
 *     LegalWorkspace).
 *   - ADR-021: Fluent v9 tokens only — no hex / rgba literals.
 *   - ADR-022: React 19 functional component.
 *   - ADR-025: Icons from `@fluentui/react-icons` v9.
 *   - ADR-028: BFF calls via `authenticatedFetch`; no token snapshots.
 */

import * as React from "react";
import {
  makeStyles,
  mergeClasses,
  tokens,
  Button,
  Spinner,
  Text,
  Tooltip,
  Input,
  Badge,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  OverlayDrawer,
  DrawerHeader,
  DrawerHeaderTitle,
  DrawerBody,
} from "@fluentui/react-components";
import {
  DismissRegular,
  PinRegular,
  PinFilled,
  EditRegular,
  DeleteRegular,
  MoreHorizontalRegular,
  StarRegular,
  ArrowUpRegular,
  ArrowDownRegular,
} from "@fluentui/react-icons";
import { useAiSession, useDispatchPaneEvent } from "@spaarke/ai-widgets";
import {
  isPinned,
  pinWorkspace,
  unpinWorkspace,
  getPinnedWorkspaces,
  setPinnedWorkspacesOrder,
  moveWorkspaceToTop,
  type PinnedWorkspace,
} from "../../services/pinnedWorkspaces";
import {
  useWorkspaceLayouts,
  type WorkspaceLayoutDto,
} from "../../hooks/useWorkspaceLayouts";
import {
  renameWorkspaceLayout,
  deleteWorkspaceLayout,
} from "../../services/workspaceLayoutMutations";
// Task 102 (2026-05-22) — shared 6-template filter constant used by BOTH the
// create wizard launch (WorkspacePaneMenu) and the edit wizard launch (here)
// so operators see the SAME 6 templates surface in both flows.
import { SPAARKEAI_TEMPLATE_FILTER } from "../../constants/workspaceTemplateFilter";

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  drawerBody: {
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: 0,
    paddingRight: 0,
    // Drawer body itself scrolls; the footer is rendered as a sibling below.
    overflowY: "auto",
    flex: "1 1 auto",
  },
  spinnerCenter: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
  },
  emptyHint: {
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    paddingTop: tokens.spacingVerticalL,
    color: tokens.colorNeutralForeground3,
    fontStyle: "italic",
  },
  sectionHeader: {
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    color: tokens.colorNeutralForeground2,
  },
  sectionDivider: {
    height: "1px",
    backgroundColor: tokens.colorNeutralStroke2,
    marginTop: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalXS,
  },
  list: {
    display: "flex",
    flexDirection: "column",
  },
  row: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomStyle: "solid",
    borderBottomWidth: "1px",
    borderBottomColor: tokens.colorNeutralStroke2,
    cursor: "pointer",
    // Hover state used by the pin icon's opacity rule below — the pin's
    // `pinIconHoverOnly` class reads from this row's :hover via a sibling
    // selector pattern: we toggle the row's hover via JS-friendly group
    // hover (Griffel does not support `:hover .child`, so we attach a
    // data attribute + use it as a class hook).
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
    // When the row is hovered, surface the hover-only pin.
    ":hover .manage-pin-hover-only": {
      opacity: 1,
    },
    // When the menu trigger is focused/open, also surface the pin so the
    // user has a coherent view of the row's state.
    ":focus-within .manage-pin-hover-only": {
      opacity: 1,
    },
  },
  nameCol: {
    flex: "1 1 auto",
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    gap: "2px",
  },
  nameLine: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    minWidth: 0,
  },
  name: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    cursor: "pointer",
  },
  systemHint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontStyle: "italic",
  },
  // R4 task 053 (B-4 / FR-07): "Modified ..." metadata line beneath the
  // workspace name. Same visual hierarchy as the systemHint above so the row
  // density stays consistent.
  modifiedHint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  renameInput: {
    width: "100%",
  },
  iconButton: {
    minWidth: "unset",
    width: "28px",
    height: "28px",
    padding: "0",
    flexShrink: 0,
  },
  // Pin icon — hover-only visibility variant. Combined with the row's
  // ":hover .manage-pin-hover-only { opacity: 1 }" rule above. We rely on
  // a static class name (NOT a Griffel-generated one) for the selector to
  // match — see the `manage-pin-hover-only` className applied in JSX.
  pinIconHoverOnly: {
    opacity: 0,
    color: tokens.colorNeutralForeground3,
    transition: "opacity 0.1s ease-in-out",
    ":hover": {
      color: tokens.colorNeutralForeground1,
    },
  },
  pinIconAlwaysVisible: {
    opacity: 1,
    color: tokens.colorBrandForeground1,
    ":hover": {
      color: tokens.colorBrandForeground1,
    },
  },
  defaultBadge: {
    flexShrink: 0,
  },
  footer: {
    display: "flex",
    justifyContent: "flex-end",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderTopStyle: "solid",
    borderTopWidth: "1px",
    borderTopColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  dangerButton: {
    backgroundColor: tokens.colorStatusDangerBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
    ":hover": {
      backgroundColor: tokens.colorStatusDangerBackground3Hover,
      color: tokens.colorNeutralForegroundOnBrand,
    },
    ":hover:active": {
      backgroundColor: tokens.colorStatusDangerBackground3Pressed,
      color: tokens.colorNeutralForegroundOnBrand,
    },
  },
  errorBanner: {
    margin: tokens.spacingHorizontalM,
    padding: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorStatusDangerBackground1,
    color: tokens.colorStatusDangerForeground1,
    borderRadius: tokens.borderRadiusMedium,
    fontSize: tokens.fontSizeBase200,
  },
});

// ---------------------------------------------------------------------------
// Modified-date formatting — R4 task 053 (B-4 / FR-07)
//
// The BFF emits `modifiedOn` as an ISO-8601 string (camelCase). We format
// CLIENT-SIDE with locale-aware helpers — never on the server — so users in
// different locales see the format they expect.
//
// Display rules:
//   - "1970-01-01T00:00:00+00:00" (Unix-epoch sentinel from
//     SystemWorkspaceLayouts.cs hard-coded layouts) → returns null. The row
//     omits the modified line entirely (system layouts already carry a
//     "System layout — Save As to edit" hint; a second metadata line would
//     be redundant).
//   - Unparseable / empty string → returns null. Defensive: don't render a
//     broken date if the wire shape ever changes unexpectedly.
//   - <60s ago → "Modified just now" (FR-07 acceptance criterion: new layouts
//     surface "just now" or near-equivalent relative time).
//   - <60m ago → "Modified Nm ago"
//   - <24h ago → "Modified Nh ago"
//   - <7d ago → "Modified Nd ago"
//   - older → "Modified {locale short date}" (e.g., "Modified 5/26/2026")
//
// Mirrors the `formatRelative` helper in HistoryOverlay.tsx for visual
// consistency across SpaarkeAi.
// ---------------------------------------------------------------------------

const UNIX_EPOCH_SENTINEL = 0;

function formatModifiedOn(iso: string): string | null {
  if (!iso) return null;
  const ts = Date.parse(iso);
  if (Number.isNaN(ts)) return null;
  // Unix-epoch sentinel = "system layout, never modified" → no display.
  if (ts === UNIX_EPOCH_SENTINEL) return null;

  const diffMs = Date.now() - ts;
  if (diffMs < 60_000) return "Modified just now";
  if (diffMs < 3_600_000)
    return `Modified ${Math.floor(diffMs / 60_000)}m ago`;
  if (diffMs < 86_400_000)
    return `Modified ${Math.floor(diffMs / 3_600_000)}h ago`;
  if (diffMs < 7 * 86_400_000)
    return `Modified ${Math.floor(diffMs / 86_400_000)}d ago`;
  return `Modified ${new Date(ts).toLocaleDateString()}`;
}

// ---------------------------------------------------------------------------
// Wizard launch helper — Xrm.Navigation.navigateTo
//
// Replicates the canonical pattern in `LegalWorkspace/src/components/Shell/
// WorkspaceGrid.tsx` (~lines 720-760). Same shape: `pageType: "webresource"`,
// webresourceName `sprk_workspacelayoutwizard`, data params encode mode +
// layoutId + bffBaseUrl + (for saveAs) layoutTemplateId + sectionsJson + name
// + templateFilter (task 102 — forces SpaarkeAi 6-template subset).
// ---------------------------------------------------------------------------

function getXrm(): unknown {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const w = window as any;
  return w?.Xrm ?? w?.parent?.Xrm ?? w?.top?.Xrm ?? null;
}

async function launchEditWizard(
  layout: WorkspaceLayoutDto,
  bffBaseUrl: string,
): Promise<void> {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const xrm = getXrm() as any;
  if (!xrm?.Navigation?.navigateTo) {
    console.warn(
      "[ManageWorkspacesPane] Xrm.Navigation.navigateTo not available — running outside Dataverse host. Edit launch is a no-op.",
    );
    return;
  }

  const mode: "edit" | "saveAs" = layout.isSystem ? "saveAs" : "edit";

  const parts: string[] = [];
  parts.push(`mode=${encodeURIComponent(mode)}`);
  parts.push(`bffBaseUrl=${encodeURIComponent(bffBaseUrl ?? "")}`);
  parts.push(`layoutId=${encodeURIComponent(layout.id)}`);
  if (mode === "saveAs") {
    parts.push(
      `layoutTemplateId=${encodeURIComponent(layout.layoutTemplateId)}`,
    );
    parts.push(`sectionsJson=${encodeURIComponent(layout.sectionsJson)}`);
    parts.push(`name=${encodeURIComponent(layout.name)}`);
  }
  parts.push(
    `templateFilter=${encodeURIComponent(SPAARKEAI_TEMPLATE_FILTER.join(","))}`,
  );
  const data = parts.join("&");

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
        title: mode === "saveAs" ? "Save As New Workspace" : "Edit Workspace",
      },
    );
  } catch (err: unknown) {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const code = (err as any)?.errorCode;
    if (code !== 2) {
      console.warn("[ManageWorkspacesPane] Wizard launch error:", err);
    }
  }
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ManageWorkspacesPaneProps {
  /** Whether the drawer is open. Owned by the parent. */
  open: boolean;
  /** Called when the drawer should close (X click, Escape, scrim click, Cancel, Save). */
  onOpenChange: (open: boolean) => void;
}

// ---------------------------------------------------------------------------
// ManageWorkspacesPane component
// ---------------------------------------------------------------------------

export const ManageWorkspacesPane: React.FC<ManageWorkspacesPaneProps> = ({
  open,
  onOpenChange,
}) => {
  const styles = useStyles();
  const { authenticatedFetch, bffBaseUrl, isAuthenticated } = useAiSession();
  const { layouts, isLoading, refetch } = useWorkspaceLayouts({
    bffBaseUrl,
    authenticatedFetch,
    isAuthenticated,
  });
  // Used by row-click → `widget_load` dispatch (mirrors WorkspacePaneMenu.handleLayoutSelect)
  const dispatch = useDispatchPaneEvent();

  // -------------------------------------------------------------------------
  // Ordered pinned list — task 104
  //
  // We hold the FULL pinned-list (ordered, with names) in React state so the
  // UI re-renders synchronously on pin/unpin/move/set-default. localStorage
  // remains the source of truth on disk; this state is kept in sync via the
  // explicit setters in handlers below.
  // -------------------------------------------------------------------------

  const [pinnedList, setPinnedList] = React.useState<PinnedWorkspace[]>(() =>
    getPinnedWorkspaces(),
  );

  // Re-sync from disk whenever the drawer opens (other surfaces may have
  // pinned/unpinned while the drawer was closed).
  React.useEffect(() => {
    if (!open) return;
    setPinnedList(getPinnedWorkspaces());
  }, [open]);

  // Derived: id-set for O(1) "is pinned?" + the id at index 0 (the default).
  const pinnedIdSet = React.useMemo(
    () => new Set(pinnedList.map((p) => p.layoutId)),
    [pinnedList],
  );
  const defaultLayoutId = pinnedList[0]?.layoutId ?? null;

  // -------------------------------------------------------------------------
  // Pin toggle — used by the always-visible pin icon AND the menu's
  // Pin / Unpin item. Persists to localStorage and re-syncs state from disk
  // so the ordered list view is always fresh.
  // -------------------------------------------------------------------------

  const handlePinToggle = React.useCallback(
    (layoutId: string, layoutName: string): void => {
      if (!layoutId) return;
      if (isPinned(layoutId)) {
        unpinWorkspace(layoutId);
      } else {
        pinWorkspace(layoutId, layoutName);
      }
      setPinnedList(getPinnedWorkspaces());
    },
    [],
  );

  // -------------------------------------------------------------------------
  // Set as default — moves to index 0 of pinned list (pins if necessary).
  // -------------------------------------------------------------------------

  const handleSetAsDefault = React.useCallback(
    (layoutId: string, layoutName: string): void => {
      if (!layoutId) return;
      moveWorkspaceToTop(layoutId, layoutName);
      setPinnedList(getPinnedWorkspaces());
    },
    [],
  );

  // -------------------------------------------------------------------------
  // Reorder — swap adjacent indices within the pinned list. No-op if the
  // workspace isn't pinned (the menu items are also disabled in that case;
  // this is a defensive guard).
  // -------------------------------------------------------------------------

  const handleMoveUp = React.useCallback((layoutId: string): void => {
    if (!layoutId) return;
    const current = getPinnedWorkspaces();
    const idx = current.findIndex((p) => p.layoutId === layoutId);
    if (idx <= 0) return; // not pinned or already at top
    const next = current.slice();
    [next[idx - 1], next[idx]] = [next[idx], next[idx - 1]];
    setPinnedWorkspacesOrder(next);
    setPinnedList(getPinnedWorkspaces());
  }, []);

  const handleMoveDown = React.useCallback((layoutId: string): void => {
    if (!layoutId) return;
    const current = getPinnedWorkspaces();
    const idx = current.findIndex((p) => p.layoutId === layoutId);
    if (idx < 0 || idx >= current.length - 1) return; // not pinned or at bottom
    const next = current.slice();
    [next[idx], next[idx + 1]] = [next[idx + 1], next[idx]];
    setPinnedWorkspacesOrder(next);
    setPinnedList(getPinnedWorkspaces());
  }, []);

  // -------------------------------------------------------------------------
  // Inline rename state — Path A (double-click to rename) — preserved from task 093
  // -------------------------------------------------------------------------

  const [renamingId, setRenamingId] = React.useState<string | null>(null);
  const [renameDraft, setRenameDraft] = React.useState<string>("");
  const [errorMsg, setErrorMsg] = React.useState<string | null>(null);

  const beginRename = React.useCallback((layout: WorkspaceLayoutDto): void => {
    if (layout.isSystem) return; // system layouts can't be renamed
    setRenamingId(layout.id);
    setRenameDraft(layout.name);
    setErrorMsg(null);
  }, []);

  const cancelRename = React.useCallback((): void => {
    setRenamingId(null);
    setRenameDraft("");
  }, []);

  const commitRename = React.useCallback(
    async (layout: WorkspaceLayoutDto): Promise<void> => {
      const trimmed = renameDraft.trim();
      if (!trimmed || trimmed === layout.name) {
        cancelRename();
        return;
      }
      try {
        await renameWorkspaceLayout(layout, trimmed, {
          bffBaseUrl,
          authenticatedFetch,
        });
        cancelRename();
        refetch();
        // If the workspace was pinned, refresh the pinned-list entry's name
        // so it doesn't go stale on disk. Re-pin with the new name (pin is
        // idempotent — refreshes display name).
        if (pinnedIdSet.has(layout.id)) {
          pinWorkspace(layout.id, trimmed);
          setPinnedList(getPinnedWorkspaces());
        }
      } catch (err: unknown) {
        const status = (err as Error & { status?: number })?.status;
        if (status === 403) {
          setErrorMsg("This workspace can't be renamed (system layout).");
        } else if (status === 404) {
          setErrorMsg("Workspace not found — it may have been deleted.");
        } else if (status === 412) {
          // R4 task 054 (B-5 / FR-08): concurrency conflict — another
          // session modified the layout. Refresh the list so the user's
          // next attempt picks up the fresh modifiedOn.
          setErrorMsg(
            "This workspace was edited elsewhere — refresh and retry.",
          );
          refetch();
        } else {
          setErrorMsg("Rename failed. Please try again.");
        }
        console.warn("[ManageWorkspacesPane] Rename failed:", err);
      }
    },
    [
      renameDraft,
      bffBaseUrl,
      authenticatedFetch,
      cancelRename,
      refetch,
      pinnedIdSet,
    ],
  );

  // -------------------------------------------------------------------------
  // Delete-confirmation dialog state — preserved from task 093
  // -------------------------------------------------------------------------

  const [deleteTarget, setDeleteTarget] =
    React.useState<WorkspaceLayoutDto | null>(null);
  const [isDeleting, setIsDeleting] = React.useState(false);

  const closeDeleteDialog = React.useCallback((): void => {
    setDeleteTarget(null);
    setIsDeleting(false);
  }, []);

  const confirmDelete = React.useCallback(async (): Promise<void> => {
    if (!deleteTarget) return;
    setIsDeleting(true);
    try {
      await deleteWorkspaceLayout(deleteTarget.id, {
        bffBaseUrl,
        authenticatedFetch,
      });
      // If the deleted layout was pinned, drop it from the pin list so the
      // cold-load auto-open effect doesn't fail to resolve it next session.
      if (pinnedIdSet.has(deleteTarget.id)) {
        unpinWorkspace(deleteTarget.id);
        setPinnedList(getPinnedWorkspaces());
      }
      closeDeleteDialog();
      refetch();
    } catch (err: unknown) {
      const status = (err as Error & { status?: number })?.status;
      if (status === 403) {
        setErrorMsg("This workspace can't be deleted (system layout).");
      } else if (status === 404) {
        setErrorMsg("Workspace not found — it may already be deleted.");
        refetch();
      } else {
        setErrorMsg("Delete failed. Please try again.");
      }
      console.warn("[ManageWorkspacesPane] Delete failed:", err);
      closeDeleteDialog();
    }
  }, [
    deleteTarget,
    bffBaseUrl,
    authenticatedFetch,
    pinnedIdSet,
    closeDeleteDialog,
    refetch,
  ]);

  // -------------------------------------------------------------------------
  // Edit — launch wizard then refetch when it closes.
  // -------------------------------------------------------------------------

  const handleEdit = React.useCallback(
    async (layout: WorkspaceLayoutDto): Promise<void> => {
      await launchEditWizard(layout, bffBaseUrl);
      refetch();
    },
    [bffBaseUrl, refetch],
  );

  // -------------------------------------------------------------------------
  // Row click — open the workspace as a new tab. Mirrors
  // WorkspacePaneMenu.handleLayoutSelect (task 102).
  // -------------------------------------------------------------------------

  const handleRowClick = React.useCallback(
    (layout: WorkspaceLayoutDto): void => {
      if (renamingId === layout.id) return; // don't dispatch while editing name
      dispatch("workspace", {
        type: "widget_load",
        widgetType: "workspace",
        widgetData: { layoutId: layout.id, layoutName: layout.name },
        displayName: layout.name,
      });
      onOpenChange(false);
    },
    [dispatch, onOpenChange, renamingId],
  );

  // -------------------------------------------------------------------------
  // Footer handlers — Cancel + Save both close the drawer. Operations are
  // committed instantly; the buttons are operator-visible exits. If we
  // later add a transactional mode, Cancel reverts and Save commits.
  // -------------------------------------------------------------------------

  const handleCancel = React.useCallback((): void => {
    onOpenChange(false);
  }, [onOpenChange]);

  const handleSave = React.useCallback((): void => {
    onOpenChange(false);
  }, [onOpenChange]);

  // -------------------------------------------------------------------------
  // Ordered display list — task 104
  //
  //   1. Pinned section: rows in getPinnedWorkspaces() order (index 0 = default)
  //   2. Unpinned section: remaining layouts in BFF order
  //
  // Section headers + divider only render when both sections are non-empty.
  // -------------------------------------------------------------------------

  const { pinnedRows, unpinnedRows } = React.useMemo(() => {
    const pinned: WorkspaceLayoutDto[] = [];
    for (const p of pinnedList) {
      const found = layouts.find((l) => l.id === p.layoutId);
      if (found) pinned.push(found);
    }
    const unpinned = layouts.filter((l) => !pinnedIdSet.has(l.id));
    return { pinnedRows: pinned, unpinnedRows: unpinned };
  }, [layouts, pinnedList, pinnedIdSet]);

  // -------------------------------------------------------------------------
  // Per-row renderer — extracted to keep the render JSX readable
  // -------------------------------------------------------------------------

  const renderRow = (layout: WorkspaceLayoutDto): React.ReactElement => {
    const layoutIsPinned = pinnedIdSet.has(layout.id);
    const isDefault = defaultLayoutId === layout.id;
    const isRenaming = renamingId === layout.id;
    const pinIdx = layoutIsPinned
      ? pinnedList.findIndex((p) => p.layoutId === layout.id)
      : -1;
    const canMoveUp = layoutIsPinned && pinIdx > 0;
    const canMoveDown =
      layoutIsPinned && pinIdx >= 0 && pinIdx < pinnedList.length - 1;
    const pinTooltip = layoutIsPinned
      ? `Unpin ${layout.name}`
      : `Pin ${layout.name}`;
    const deleteTooltip = layout.isSystem
      ? "System layouts can't be deleted — use Edit then Save As to clone."
      : `Delete ${layout.name}`;
    const editTooltip = layout.isSystem
      ? `Save ${layout.name} as a new editable workspace`
      : `Edit ${layout.name}`;

    return (
      <div
        key={layout.id}
        className={styles.row}
        data-testid={`manage-workspaces-row-${layout.id}`}
        onClick={() => handleRowClick(layout)}
        role="button"
        tabIndex={0}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            handleRowClick(layout);
          }
        }}
      >
        {/* Pin icon — hover-only when unpinned, always-visible when pinned */}
        <Tooltip content={pinTooltip} relationship="label">
          <Button
            className={mergeClasses(
              styles.iconButton,
              layoutIsPinned
                ? styles.pinIconAlwaysVisible
                : styles.pinIconHoverOnly,
              // Static class hook for the row's :hover .manage-pin-hover-only
              // selector. Griffel-generated class names are non-deterministic
              // so we use a stable className alongside the Griffel one.
              !layoutIsPinned && "manage-pin-hover-only",
            )}
            appearance="subtle"
            icon={layoutIsPinned ? <PinFilled /> : <PinRegular />}
            aria-label={pinTooltip}
            aria-pressed={layoutIsPinned}
            onClick={(e) => {
              e.stopPropagation();
              handlePinToggle(layout.id, layout.name);
            }}
            data-testid={`manage-pin-${layout.id}`}
          />
        </Tooltip>

        {/* Name + secondary metadata + (optional) Default badge */}
        <div className={styles.nameCol}>
          {isRenaming ? (
            <Input
              className={styles.renameInput}
              value={renameDraft}
              autoFocus
              onClick={(e) => e.stopPropagation()}
              onChange={(_, data) => setRenameDraft(data.value)}
              onKeyDown={(e) => {
                e.stopPropagation();
                if (e.key === "Enter") {
                  e.preventDefault();
                  void commitRename(layout);
                } else if (e.key === "Escape") {
                  e.preventDefault();
                  cancelRename();
                }
              }}
              onBlur={() => void commitRename(layout)}
              data-testid={`manage-rename-input-${layout.id}`}
            />
          ) : (
            <div className={styles.nameLine}>
              <Text
                className={styles.name}
                weight="semibold"
                onDoubleClick={(e) => {
                  e.stopPropagation();
                  beginRename(layout);
                }}
                title={
                  layout.isSystem
                    ? layout.name
                    : `${layout.name} (double-click to rename)`
                }
              >
                {layout.name}
              </Text>
              {isDefault && (
                <Badge
                  appearance="tint"
                  color="brand"
                  size="small"
                  className={styles.defaultBadge}
                  data-testid={`manage-default-badge-${layout.id}`}
                >
                  Default
                </Badge>
              )}
            </div>
          )}
          {layout.isSystem && (
            <Text className={styles.systemHint}>
              System layout — Save As to edit
            </Text>
          )}
          {/* R4 task 053 (B-4 / FR-07): "Modified ..." metadata line.
            * Hidden for system layouts (their modifiedOn is the Unix-epoch
            * sentinel — see formatModifiedOn) and during inline rename
            * (less visual noise on the row that's being edited). */}
          {!layout.isSystem &&
            !isRenaming &&
            (() => {
              const modifiedLabel = formatModifiedOn(layout.modifiedOn);
              return modifiedLabel ? (
                <Text
                  className={styles.modifiedHint}
                  data-testid={`manage-modified-${layout.id}`}
                >
                  {modifiedLabel}
                </Text>
              ) : null;
            })()}
        </div>

        {/* Three-dot (⋯) action menu */}
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <Tooltip content="More actions" relationship="label">
              <Button
                className={styles.iconButton}
                appearance="subtle"
                icon={<MoreHorizontalRegular />}
                aria-label={`Actions for ${layout.name}`}
                onClick={(e) => e.stopPropagation()}
                data-testid={`manage-more-${layout.id}`}
              />
            </Tooltip>
          </MenuTrigger>
          <MenuPopover onClick={(e) => e.stopPropagation()}>
            <MenuList>
              {/* 1. Pin / Unpin */}
              <MenuItem
                icon={layoutIsPinned ? <PinFilled /> : <PinRegular />}
                onClick={() => handlePinToggle(layout.id, layout.name)}
                data-testid={`manage-menu-pin-${layout.id}`}
              >
                {layoutIsPinned ? "Unpin" : "Pin"}
              </MenuItem>

              {/* 2. Set as default */}
              <Tooltip
                content={
                  isDefault
                    ? "Already the default workspace."
                    : "Move to the top of the pinned list."
                }
                relationship="description"
              >
                <MenuItem
                  icon={<StarRegular />}
                  disabled={isDefault}
                  aria-disabled={isDefault}
                  onClick={() =>
                    !isDefault && handleSetAsDefault(layout.id, layout.name)
                  }
                  data-testid={`manage-menu-default-${layout.id}`}
                >
                  Set as default
                </MenuItem>
              </Tooltip>

              {/* 3. Move up */}
              <Tooltip
                content={
                  !layoutIsPinned
                    ? "Pin this workspace first to reorder it."
                    : canMoveUp
                      ? "Move up in pinned order."
                      : "Already at the top."
                }
                relationship="description"
              >
                <MenuItem
                  icon={<ArrowUpRegular />}
                  disabled={!canMoveUp}
                  aria-disabled={!canMoveUp}
                  onClick={() => canMoveUp && handleMoveUp(layout.id)}
                  data-testid={`manage-menu-up-${layout.id}`}
                >
                  Move up
                </MenuItem>
              </Tooltip>

              {/* 4. Move down */}
              <Tooltip
                content={
                  !layoutIsPinned
                    ? "Pin this workspace first to reorder it."
                    : canMoveDown
                      ? "Move down in pinned order."
                      : "Already at the bottom."
                }
                relationship="description"
              >
                <MenuItem
                  icon={<ArrowDownRegular />}
                  disabled={!canMoveDown}
                  aria-disabled={!canMoveDown}
                  onClick={() => canMoveDown && handleMoveDown(layout.id)}
                  data-testid={`manage-menu-down-${layout.id}`}
                >
                  Move down
                </MenuItem>
              </Tooltip>

              {/* 5. Edit */}
              <Tooltip content={editTooltip} relationship="description">
                <MenuItem
                  icon={<EditRegular />}
                  onClick={() => void handleEdit(layout)}
                  data-testid={`manage-menu-edit-${layout.id}`}
                >
                  Edit
                </MenuItem>
              </Tooltip>

              {/* 6. Delete */}
              <Tooltip content={deleteTooltip} relationship="description">
                <MenuItem
                  icon={<DeleteRegular />}
                  disabled={layout.isSystem}
                  aria-disabled={layout.isSystem}
                  onClick={() =>
                    !layout.isSystem && setDeleteTarget(layout)
                  }
                  data-testid={`manage-menu-delete-${layout.id}`}
                >
                  Delete
                </MenuItem>
              </Tooltip>
            </MenuList>
          </MenuPopover>
        </Menu>
      </div>
    );
  };

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  const showSectionHeaders =
    pinnedRows.length > 0 && unpinnedRows.length > 0;

  return (
    <>
      <OverlayDrawer
        open={open}
        position="end"
        size="medium"
        onOpenChange={(_, data) => onOpenChange(data.open)}
        data-testid="manage-workspaces-drawer"
      >
        <DrawerHeader>
          <DrawerHeaderTitle
            action={
              <Button
                appearance="subtle"
                aria-label="Close"
                icon={<DismissRegular />}
                onClick={() => onOpenChange(false)}
                data-testid="manage-workspaces-close"
              />
            }
          >
            Manage Workspaces
          </DrawerHeaderTitle>
        </DrawerHeader>

        <DrawerBody className={styles.drawerBody}>
          {errorMsg && (
            <div role="alert" className={styles.errorBanner}>
              {errorMsg}
            </div>
          )}

          {isLoading && layouts.length === 0 ? (
            <div className={styles.spinnerCenter}>
              <Spinner size="small" label="Loading workspaces..." />
            </div>
          ) : layouts.length === 0 ? (
            <Text className={styles.emptyHint}>
              No workspaces yet. Use "+ New Workspace" in the Workspaces menu
              to create one.
            </Text>
          ) : (
            <div data-testid="manage-workspaces-list">
              {pinnedRows.length > 0 && (
                <>
                  {showSectionHeaders && (
                    <Text
                      as="h3"
                      size={200}
                      weight="semibold"
                      block
                      className={styles.sectionHeader}
                      data-testid="manage-section-pinned-header"
                    >
                      Pinned
                    </Text>
                  )}
                  <div className={styles.list}>
                    {pinnedRows.map(renderRow)}
                  </div>
                </>
              )}

              {showSectionHeaders && (
                <div className={styles.sectionDivider} role="presentation" />
              )}

              {unpinnedRows.length > 0 && (
                <>
                  {showSectionHeaders && (
                    <Text
                      as="h3"
                      size={200}
                      weight="semibold"
                      block
                      className={styles.sectionHeader}
                      data-testid="manage-section-others-header"
                    >
                      All workspaces
                    </Text>
                  )}
                  <div className={styles.list}>
                    {unpinnedRows.map(renderRow)}
                  </div>
                </>
              )}
            </div>
          )}
        </DrawerBody>

        {/* Footer — Cancel + Save (instant model; both close the drawer).
         * If we later add a transactional mode, Cancel reverts and Save
         * commits. See file header for the rationale. */}
        <div className={styles.footer}>
          <Tooltip
            content="Close without applying further changes."
            relationship="description"
          >
            <Button
              appearance="secondary"
              onClick={handleCancel}
              data-testid="manage-workspaces-cancel"
            >
              Cancel
            </Button>
          </Tooltip>
          <Tooltip
            content="Close the manage panel."
            relationship="description"
          >
            <Button
              appearance="primary"
              onClick={handleSave}
              data-testid="manage-workspaces-save"
            >
              Save
            </Button>
          </Tooltip>
        </div>
      </OverlayDrawer>

      {/* Inline delete-confirmation dialog (preserved from task 093). */}
      <Dialog
        open={deleteTarget !== null}
        onOpenChange={(_, data) => {
          if (!data.open) closeDeleteDialog();
        }}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Delete workspace?</DialogTitle>
            <DialogContent>
              <Text>
                <strong>{deleteTarget?.name}</strong> will be permanently
                removed. This cannot be undone.
              </Text>
            </DialogContent>
            <DialogActions>
              <Button
                appearance="secondary"
                onClick={closeDeleteDialog}
                disabled={isDeleting}
                data-testid="manage-delete-cancel"
              >
                Cancel
              </Button>
              <Button
                appearance="primary"
                className={styles.dangerButton}
                onClick={() => void confirmDelete()}
                disabled={isDeleting}
                data-testid="manage-delete-confirm"
              >
                {isDeleting ? "Deleting..." : "Delete"}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </>
  );
};

ManageWorkspacesPane.displayName = "ManageWorkspacesPane";
