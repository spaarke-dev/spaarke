/**
 * SmartToDoDialog — LegalWorkspace thin shim over the hoisted
 * `@spaarke/smart-todo-components` `SmartToDoDialog`.
 *
 * R5 FR-01 / task 003 (thin-shim conversion). Preserves the pre-hoist public
 * surface (`open`, `onClose`, `webApi`, `userId`) so the existing caller
 * (`components/Shell/WorkspaceGrid.tsx`'s `LazySmartToDoDialog`) requires no
 * changes. Builds the injected `smartTodoProps` bag via the SAME
 * `useSmartToDoBridge` hook the `SmartToDo` shim uses (single source of
 * wiring — no duplicated coupling logic between the two shims).
 *
 * Background: prior to the W-6 retirement of the standalone
 * `sprk_corporateworkspace` web resource, "Open To Do Dialog" navigated to a
 * 90%×90% dialog webresource. This shim renders the hoisted package dialog
 * inline within the SpaarkeAi shell instead — no navigation away from the
 * host page. See task-044 history in the pre-hoist component for full
 * background (superseded by this file).
 */

import * as React from "react";
import { SmartToDoDialog as PackageSmartToDoDialog } from "@spaarke/smart-todo-components";
import { useSmartToDoBridge } from "../../hooks/useSmartToDoBridge";
import type { IWebApi } from "../../types/xrm";

export interface ISmartToDoDialogProps {
  /** Controls dialog visibility. */
  open: boolean;
  /** Invoked when the user closes the dialog (X button, ESC, or backdrop). */
  onClose: () => void;
  /** Xrm.WebApi reference forwarded to the injected SmartToDo bridge. */
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId). */
  userId: string;
}

export const SmartToDoDialog: React.FC<ISmartToDoDialogProps> = ({
  open,
  onClose,
  webApi,
  userId,
}) => {
  const smartTodoProps = useSmartToDoBridge({ webApi, userId });

  return (
    <PackageSmartToDoDialog
      open={open}
      onClose={onClose}
      smartTodoProps={smartTodoProps}
    />
  );
};

SmartToDoDialog.displayName = "SmartToDoDialog";

export default SmartToDoDialog;
