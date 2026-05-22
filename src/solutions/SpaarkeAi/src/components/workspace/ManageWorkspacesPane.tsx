/**
 * ManageWorkspacesPane.tsx — Fluent v9 OverlayDrawer right-side panel listing
 * the user's workspace layouts with per-row management actions (task 093 —
 * operator UX iteration 2026-05-22, Wave 3).
 *
 * # Why this exists (R-30 resolution)
 *
 * Operator feedback 2026-05-22: "Need to be able to delete workspace. Manage
 * workspaces UI similar to view manager pattern... Move the 'Edit' workspace
 * to the Manage workspace. Move the delete workspace to the Manage workspace."
 *
 * Task 098 removed the "Edit current workspace" dropdown entry and left the
 * "Manage workspaces" entry's onClick as a `console.log` stub pointing here.
 * This component closes that loop: clicking Manage workspaces in the
 * Workspaces dropdown opens this side pane.
 *
 * # Layout
 *
 * Fluent v9 `<OverlayDrawer>` anchored to the right edge of the viewport.
 * Width 440px so each row comfortably fits: pin star + name (with optional
 * secondary metadata line) + edit + delete affordances. Width matches the
 * operator's "view manager pattern" reference visual.
 *
 * Each row:
 *   - Pin star (left) — `PinRegular` (unpinned) / `PinFilled` (pinned, brand
 *     color). Wired to the SAME `pinnedWorkspaces.ts` API used by the
 *     dropdown (task 098) — no parallel storage. Clicking the pin updates
 *     the localStorage list that the cold-load auto-open effect in
 *     `WorkspacePane.tsx` consumes (task 092). Operator's "Default = Pin"
 *     semantics: pinning a workspace marks it as a default-on-load.
 *
 *   - Workspace name — double-click to rename inline (Path A — fastest UX).
 *     System layouts cannot be renamed; double-click is a no-op for them.
 *     Enter commits the rename via `renameWorkspaceLayout`; Escape cancels.
 *
 *   - Secondary metadata — "System layout" inline hint when applicable. The
 *     BFF DTO does NOT currently carry `modifiedOn` (it would need a DTO
 *     extension we deliberately did NOT add per CLAUDE.md §10 BFF Hygiene),
 *     so we surface the system/user distinction instead of a date.
 *
 *   - Edit icon — `EditRegular`. Launches the WorkspaceLayoutWizard via
 *     `Xrm.Navigation.navigateTo`. For system layouts, triggers `saveAs`
 *     mode (clone-then-edit) following the WorkspaceGrid.tsx pattern in
 *     LegalWorkspace. For user layouts, `edit` mode in place.
 *
 *   - Delete icon — `DeleteRegular`. Opens an inline `<Dialog>` confirmation
 *     (red primary button). System layouts: button disabled with a Tooltip
 *     explaining why ("System layouts can't be deleted — use Edit then Save
 *     As to create your own copy.").
 *
 * After any mutation (rename / delete / wizard close), the parent's
 * `useWorkspaceLayouts.refetch()` is invoked so the list re-syncs from the BFF.
 *
 * # BFF integration
 *
 * Uses `useAiSession()` for `authenticatedFetch` + `bffBaseUrl` (ADR-028).
 * All four endpoints PRE-EXIST in `Sprk.Bff.Api/Api/Workspace/WorkspaceLayoutEndpoints.cs`
 * (PUT for rename, DELETE for delete, plus the GET endpoints the hook already
 * uses). No new BFF endpoints were added per CLAUDE.md §10 BFF Hygiene.
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
 *
 * # Reuse / DRY
 *
 *   - Pin toggle uses `pinnedWorkspaces.ts` (same API as
 *     `WorkspacePaneMenu.tsx` — task 098).
 *   - Edit wizard launch replicates `WorkspaceGrid.tsx` (LegalWorkspace) and
 *     the pre-task-098 `handleEditWorkspace` logic that lived in
 *     `WorkspacePaneMenu.tsx`. The wizard navigation pattern is
 *     intentionally duplicated rather than extracted to a shared helper —
 *     the two surfaces have slightly different data-param shapes and the
 *     duplicate is ~30 LOC.
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
} from "@fluentui/react-icons";
import { useAiSession } from "@spaarke/ai-widgets";
import {
  isPinned,
  pinWorkspace,
  unpinWorkspace,
  getPinnedWorkspaces,
} from "../../services/pinnedWorkspaces";
import {
  useWorkspaceLayouts,
  type WorkspaceLayoutDto,
} from "../../hooks/useWorkspaceLayouts";
import {
  renameWorkspaceLayout,
  deleteWorkspaceLayout,
} from "../../services/workspaceLayoutMutations";

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  drawerBody: {
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: 0,
    paddingRight: 0,
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
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
  },
  nameCol: {
    flex: "1 1 auto",
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    gap: "2px",
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
  pinIconButton: {
    color: tokens.colorNeutralForeground3,
    ":hover": {
      color: tokens.colorNeutralForeground1,
    },
  },
  pinIconButtonActive: {
    color: tokens.colorBrandForeground1,
    ":hover": {
      color: tokens.colorBrandForeground1,
    },
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
// Wizard launch helper — Xrm.Navigation.navigateTo
//
// Replicates the canonical pattern in `LegalWorkspace/src/components/Shell/
// WorkspaceGrid.tsx` (lines ~720-760) and the pre-task-098 `handleEditWorkspace`
// logic in `WorkspacePaneMenu.tsx`. Same shape: `pageType: "webresource"`,
// webresourceName `sprk_workspacelayoutwizard`, data params encode mode +
// layoutId + bffBaseUrl + (for saveAs) layoutTemplateId + sectionsJson + name.
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

  // System layouts use saveAs mode (clone-then-edit); user layouts use edit
  // in place. Mirrors LegalWorkspace's WorkspaceGrid.tsx logic verbatim.
  const mode: "edit" | "saveAs" = layout.isSystem ? "saveAs" : "edit";

  const parts: string[] = [];
  parts.push(`mode=${encodeURIComponent(mode)}`);
  parts.push(`bffBaseUrl=${encodeURIComponent(bffBaseUrl ?? "")}`);
  parts.push(`layoutId=${encodeURIComponent(layout.id)}`);
  if (mode === "saveAs") {
    // SaveAs pre-populates all three wizard steps from the source layout.
    parts.push(
      `layoutTemplateId=${encodeURIComponent(layout.layoutTemplateId)}`,
    );
    parts.push(`sectionsJson=${encodeURIComponent(layout.sectionsJson)}`);
    parts.push(`name=${encodeURIComponent(layout.name)}`);
  }
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
  /** Called when the drawer should close (X click, Escape, scrim click). */
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

  // -------------------------------------------------------------------------
  // Pin state — same pattern as WorkspacePaneMenu (task 098)
  // -------------------------------------------------------------------------

  const [pinnedIds, setPinnedIds] = React.useState<Set<string>>(() => {
    const set = new Set<string>();
    for (const p of getPinnedWorkspaces()) set.add(p.layoutId);
    return set;
  });

  // Re-sync pin state whenever the drawer opens so a freshly-pinned layout
  // from the dropdown is reflected here without remounting.
  React.useEffect(() => {
    if (!open) return;
    const next = new Set<string>();
    for (const p of getPinnedWorkspaces()) next.add(p.layoutId);
    setPinnedIds(next);
  }, [open]);

  const handlePinToggle = React.useCallback(
    (layoutId: string, layoutName: string): void => {
      if (!layoutId) return;
      if (isPinned(layoutId)) {
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
    [],
  );

  // -------------------------------------------------------------------------
  // Inline rename state — Path A (double-click to rename)
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
      // No-op if the user pressed Enter without changes
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
      } catch (err: unknown) {
        const status = (err as Error & { status?: number })?.status;
        if (status === 403) {
          setErrorMsg("This workspace can't be renamed (system layout).");
        } else if (status === 404) {
          setErrorMsg("Workspace not found — it may have been deleted.");
        } else {
          setErrorMsg("Rename failed. Please try again.");
        }
        console.warn("[ManageWorkspacesPane] Rename failed:", err);
      }
    },
    [renameDraft, bffBaseUrl, authenticatedFetch, cancelRename, refetch],
  );

  // -------------------------------------------------------------------------
  // Delete-confirmation dialog state
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
      if (pinnedIds.has(deleteTarget.id)) {
        unpinWorkspace(deleteTarget.id);
        setPinnedIds((prev) => {
          const next = new Set(prev);
          next.delete(deleteTarget.id);
          return next;
        });
      }
      closeDeleteDialog();
      refetch();
    } catch (err: unknown) {
      const status = (err as Error & { status?: number })?.status;
      if (status === 403) {
        setErrorMsg("This workspace can't be deleted (system layout).");
      } else if (status === 404) {
        setErrorMsg("Workspace not found — it may already be deleted.");
        // Refresh anyway so the list re-syncs.
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
    pinnedIds,
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
  // Render
  // -------------------------------------------------------------------------

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
            <div className={styles.list} data-testid="manage-workspaces-list">
              {layouts.map((layout) => {
                const layoutIsPinned = pinnedIds.has(layout.id);
                const isRenaming = renamingId === layout.id;
                const pinTooltip = layoutIsPinned
                  ? `Unpin ${layout.name}`
                  : `Pin ${layout.name} as default`;
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
                  >
                    {/* Pin star */}
                    <Tooltip content={pinTooltip} relationship="label">
                      <Button
                        className={mergeClasses(
                          styles.iconButton,
                          styles.pinIconButton,
                          layoutIsPinned && styles.pinIconButtonActive,
                        )}
                        appearance="subtle"
                        icon={
                          layoutIsPinned ? <PinFilled /> : <PinRegular />
                        }
                        aria-label={pinTooltip}
                        aria-pressed={layoutIsPinned}
                        onClick={() =>
                          handlePinToggle(layout.id, layout.name)
                        }
                        data-testid={`manage-pin-${layout.id}`}
                      />
                    </Tooltip>

                    {/* Name + secondary metadata */}
                    <div className={styles.nameCol}>
                      {isRenaming ? (
                        <Input
                          className={styles.renameInput}
                          value={renameDraft}
                          autoFocus
                          onChange={(_, data) => setRenameDraft(data.value)}
                          onKeyDown={(e) => {
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
                        <Text
                          className={styles.name}
                          weight="semibold"
                          onDoubleClick={() => beginRename(layout)}
                          title={
                            layout.isSystem
                              ? layout.name
                              : `${layout.name} (double-click to rename)`
                          }
                        >
                          {layout.name}
                        </Text>
                      )}
                      {layout.isSystem && (
                        <Text className={styles.systemHint}>
                          System layout — Save As to edit
                        </Text>
                      )}
                    </div>

                    {/* Edit */}
                    <Tooltip content={editTooltip} relationship="label">
                      <Button
                        className={styles.iconButton}
                        appearance="subtle"
                        icon={<EditRegular />}
                        aria-label={editTooltip}
                        onClick={() => void handleEdit(layout)}
                        data-testid={`manage-edit-${layout.id}`}
                      />
                    </Tooltip>

                    {/* Delete */}
                    <Tooltip content={deleteTooltip} relationship="label">
                      <Button
                        className={styles.iconButton}
                        appearance="subtle"
                        icon={<DeleteRegular />}
                        aria-label={deleteTooltip}
                        disabled={layout.isSystem}
                        onClick={() => setDeleteTarget(layout)}
                        data-testid={`manage-delete-${layout.id}`}
                      />
                    </Tooltip>
                  </div>
                );
              })}
            </div>
          )}
        </DrawerBody>
      </OverlayDrawer>

      {/* Inline delete-confirmation dialog. Kept inside this file rather than
       * factored out — it's <50 lines and tightly bound to this pane's state
       * (deleteTarget / isDeleting / refetch). */}
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
