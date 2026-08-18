/**
 * SmartToDoDialog — Inline modal wrapper around the SmartToDo Kanban board.
 *
 * R5 FR-01 / task 002 — hoisted host-agnostic from
 * `src/solutions/LegalWorkspace/src/components/SmartToDo/SmartToDoDialog.tsx`
 * into `@spaarke/smart-todo-components`.
 *
 * HOST-AGNOSTIC CHANGE: the pre-hoist dialog took `{ webApi, userId }` and
 * rendered `<SmartToDo webApi userId />` (SmartToDo owned the data hooks). Post
 * host-agnostic redesign, `SmartToDo` receives its data + callbacks via props,
 * so this dialog simply forwards a fully-formed `ISmartToDoProps` bag (assembled
 * by the host shim, task 003). No `Xrm` / `src/solutions/...` coupling remains.
 *
 * Behavior parity with the retired `?mode=todo` page: renders the full Kanban
 * inside a 90% × 90% Fluent v9 Dialog — no navigation away from the host page.
 *
 * Constraints honored:
 *   - ADR-012: host-agnostic shared component (data bound by the host).
 *   - ADR-021: Fluent v9 primitives only, semantic tokens only, no hex/rgb.
 */

import * as React from 'react';
import { Dialog, DialogSurface, DialogTitle, DialogBody, Button, makeStyles, tokens } from '@fluentui/react-components';
import { DismissRegular } from '@fluentui/react-icons';
import { SmartToDo, type ISmartToDoProps } from './SmartToDo';

export interface ISmartToDoDialogProps {
  /** Controls dialog visibility. */
  open: boolean;
  /** Invoked when the user closes the dialog (X button, ESC, or backdrop). */
  onClose: () => void;
  /**
   * Fully-formed props for the embedded `<SmartToDo>` board, assembled by the
   * host shim (data hooks + mutation callbacks + optional services). Forwarded
   * verbatim — this wrapper adds only the modal chrome.
   */
  smartTodoProps: ISmartToDoProps;
}

// ---------------------------------------------------------------------------
// Styles — sized to match the retired navigateTo dialog dimensions
// (90% × 90% of the host page viewport). All values via Fluent v9 tokens.
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * DialogSurface override: 90vw × 90vh, with the max-width override needed
   * because Fluent's default DialogSurface caps at ~600px.
   */
  surface: {
    width: '90vw',
    height: '90vh',
    maxWidth: '90vw',
    display: 'flex',
    flexDirection: 'column',
  },
  /**
   * DialogBody takes the remaining vertical space below the title bar.
   * SmartToDo's Kanban board uses flex-grow internally; this wrapper just
   * needs to expose a non-zero height so the board can fill it.
   */
  body: {
    flex: '1 1 auto',
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
    minHeight: 0,
  },
  /**
   * Inner host for SmartToDo. The component uses its own card styling, so
   * we provide a tokenized neutral background that adapts to dark mode.
   */
  host: {
    flex: '1 1 auto',
    display: 'flex',
    flexDirection: 'column',
    minHeight: 0,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SmartToDoDialog: React.FC<ISmartToDoDialogProps> = ({ open, onClose, smartTodoProps }) => {
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
          action={<Button appearance="subtle" aria-label="Close" icon={<DismissRegular />} onClick={onClose} />}
        >
          To Do
        </DialogTitle>

        <DialogBody className={styles.body}>
          <div className={styles.host}>
            <SmartToDo {...smartTodoProps} />
          </div>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

SmartToDoDialog.displayName = 'SmartToDoDialog';

export default SmartToDoDialog;
