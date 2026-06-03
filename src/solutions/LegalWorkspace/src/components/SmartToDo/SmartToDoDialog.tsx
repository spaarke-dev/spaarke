/**
 * SmartToDoDialog — Inline modal wrapper around the SmartToDo Kanban board.
 *
 * Background / why this exists (R4 task 044):
 *   Prior to the W-6 retirement of the standalone `sprk_corporateworkspace`
 *   web resource, the WorkspaceGrid's "Open To Do Dialog" handler called
 *   `Xrm.Navigation.navigateTo({ pageType: "webresource",
 *   webresourceName: "sprk_corporateworkspace", data: "mode=todo" }, ...)`
 *   to open a 90%×90% dialog hosting the full Kanban experience. With the
 *   web resource retired, that navigateTo would 404. Per operator decision
 *   (2026-05-26, option B from the consumer audit), this modal replaces the
 *   navigateTo with an inline Fluent v9 Dialog rendered inside the active
 *   SpaarkeAi shell — no navigation away from the host page.
 *
 * Behavior parity with the retired `?mode=todo` page:
 *   The retired App.tsx mode === "todo" branch rendered
 *     <FeedTodoSyncProvider><SmartToDo webApi userId /></FeedTodoSyncProvider>
 *   This dialog renders <SmartToDo webApi userId /> directly. The
 *   FeedTodoSyncProvider is already mounted at LegalWorkspace App.tsx level
 *   (wrapping WorkspaceGrid), so the dialog inherits the same provider scope
 *   and the same flag-sync behavior the retired page had.
 *
 * Data, filters, actions — all sourced from the existing SmartToDo component.
 * No new BFF endpoints, no new services, no new DI registrations.
 *
 * Constraints honored:
 *   - ADR-012: solution-local (LegalWorkspace) — SmartToDo + its hooks live
 *     here, no `@spaarke/legal-workspace` shared package exists.
 *   - ADR-021: Fluent v9 primitives only, semantic tokens only, no hex/rgb.
 *   - ADR-022: React 19 (Code Pages context).
 *   - CLAUDE.md §10: no BFF additions — purely client-side refactor.
 *
 * @see WorkspaceGrid.tsx#handleOpenTodoDialog for the new caller.
 * @see ../../App.tsx#mode==="todo" for the retired branch this replaces.
 * @see docs/architecture/LEGALWORKSPACE-RETIREMENT.md §5 (migration guidance).
 */

import * as React from "react";
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  Button,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { DismissRegular } from "@fluentui/react-icons";
import { SmartToDo } from "./SmartToDo";
import type { IWebApi } from "../../types/xrm";

export interface ISmartToDoDialogProps {
  /** Controls dialog visibility. */
  open: boolean;
  /** Invoked when the user closes the dialog (X button, ESC, or backdrop). */
  onClose: () => void;
  /** Xrm.WebApi reference forwarded to SmartToDo. */
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId). */
  userId: string;
}

// ---------------------------------------------------------------------------
// Styles — sized to match the retired navigateTo dialog dimensions
// (90% × 90% of the host page viewport). All values via Fluent v9 tokens.
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * DialogSurface override: 90vw × 90vh, with the max-width override needed
   * because Fluent's default DialogSurface caps at ~600px. The retired
   * navigateTo opened a 90%×90% dialog; we preserve that footprint.
   */
  surface: {
    width: "90vw",
    height: "90vh",
    maxWidth: "90vw",
    display: "flex",
    flexDirection: "column",
  },
  /**
   * DialogBody takes the remaining vertical space below the title bar.
   * SmartToDo's Kanban board uses flex-grow internally; this wrapper just
   * needs to expose a non-zero height so the board can fill it.
   */
  body: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    minHeight: 0,
    // No padding override — DialogBody applies its own consistent token spacing.
  },
  /**
   * Inner host for SmartToDo. The component uses its own card styling, so
   * we provide a tokenized neutral background that adapts to dark mode.
   */
  host: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    minHeight: 0,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SmartToDoDialog: React.FC<ISmartToDoDialogProps> = ({
  open,
  onClose,
  webApi,
  userId,
}) => {
  const styles = useStyles();

  return (
    <Dialog
      open={open}
      onOpenChange={(_e, data) => {
        if (!data.open) onClose();
      }}
      modalType="modal"
    >
      <DialogSurface className={styles.surface} aria-label="To Do">
        <DialogTitle
          action={
            <Button
              appearance="subtle"
              aria-label="Close"
              icon={<DismissRegular />}
              onClick={onClose}
            />
          }
        >
          To Do
        </DialogTitle>

        <DialogBody className={styles.body}>
          <div className={styles.host}>
            <SmartToDo webApi={webApi} userId={userId} />
          </div>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

SmartToDoDialog.displayName = "SmartToDoDialog";

export default SmartToDoDialog;
