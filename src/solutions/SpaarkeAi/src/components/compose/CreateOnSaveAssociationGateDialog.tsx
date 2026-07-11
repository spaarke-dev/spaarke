/**
 * CreateOnSaveAssociationGateDialog.tsx
 *
 * FR-05 — the Tier-2c GATE DIALOG that HOSTS the create-on-save optional
 * parent-association picker.
 *
 * This is the "gate-dialog-hosting half" the original task 014 split out and
 * guarded (see `gateAssociationContract.ts`). It renders `CreateOnSaveAssociationPrompt`
 * inside the ONE confirmation dialog surface (design.md §2.4 — "no bespoke
 * confirmation banners anywhere"), so the user can optionally pick a parent
 * matter / project / invoice / work assignment for the document that was just
 * created on Save.
 *
 * WHY A Fluent v9 `Dialog` (and not `ActionConfirmationDialog`): the shipped
 * Tier-2c dialog (`ActionConfirmationDialog`) lives inside `SprkChat`
 * (`@spaarke/ui-components`) and is driven entirely by the chat action-handler
 * pipeline — it is not a standalone, mountable gate surface a compose host can
 * open imperatively for a create-on-save event. This dialog is the compose
 * create-on-save gate: a thin Fluent v9 `Dialog` (Tier-2c confirm surface, no
 * bespoke banner) that embeds the SAME `CreateOnSaveAssociationPrompt` content
 * section, reusing the SAME `AssociateToStep` record picker every Create*Wizard
 * uses (CLAUDE.md §11 — reuse, do not fork a new picker).
 *
 * Save is NEVER blocked on a parent (spec FR-05): the document is already
 * persisted by the time this gate opens (ComposeWorkspace dispatches
 * `saveSucceeded` BEFORE calling `onCreateOnSaveComplete`), so this is a purely
 * optional post-save association step. "None" + Done — or dismissing / Skip —
 * both leave a valid standalone document.
 *
 * @see CreateOnSaveAssociationPrompt.tsx — the embedded picker content
 * @see useCreateOnSaveAssociationGate.ts — the hook that owns open-state + the write
 * @see ADR-021 — Fluent v9 + dark mode (semantic tokens only; theme owned by the host FluentProvider)
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  MessageBar,
  MessageBarBody,
  Spinner,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import type { AssociationResult, EntityTypeOption, INavigationService } from '@spaarke/ui-components';
import { CreateOnSaveAssociationPrompt } from './CreateOnSaveAssociationPrompt';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateOnSaveAssociationGateDialogProps {
  /** Whether the gate dialog is open (a create-on-save just completed). */
  open: boolean;
  /** Navigation service the embedded `AssociateToStep` uses to open the Dataverse lookup. */
  navigationService: INavigationService;
  /** Current selection (controlled). `null` == "none" / standalone. */
  association: AssociationResult | null;
  /** Fired when the selection changes -- wire directly to `useCreateOnSaveAssociation.setAssociation`. */
  onChange: (result: AssociationResult | null) => void;
  /** Confirm ("Done"): write the current selection onto the new document (no-op on "none"). */
  onConfirm: () => void;
  /** Skip / dismiss: close without writing (the document stays a valid standalone). */
  onSkip: () => void;
  /** True while the association write is in flight (disables the controls + shows a spinner). */
  isAssociating?: boolean;
  /** Non-fatal write error surfaced from the hook (the document already exists). */
  error?: string | null;
  /** Optional restriction on which parent types are offered (from a `GateAssociationAffordance`). */
  allowedTargets?: ReadonlyArray<EntityTypeOption>;
}

// ---------------------------------------------------------------------------
// Styles (Fluent v9 tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  content: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * The Tier-2c gate dialog hosting the optional parent-association picker.
 *
 * @example
 * ```tsx
 * const { onCreateOnSaveComplete, dialogProps } = useCreateOnSaveAssociationGate();
 * // ...pass onCreateOnSaveComplete into ComposeLaunchContext...
 * <CreateOnSaveAssociationGateDialog {...dialogProps} />
 * ```
 */
export const CreateOnSaveAssociationGateDialog: React.FC<ICreateOnSaveAssociationGateDialogProps> = ({
  open,
  navigationService,
  association,
  onChange,
  onConfirm,
  onSkip,
  isAssociating = false,
  error = null,
  allowedTargets,
}) => {
  const styles = useStyles();

  return (
    <Dialog
      open={open}
      // A dismiss (Escape / clicking the backdrop) is treated as "Skip" -- the
      // document is already saved, so leaving without a parent is a valid outcome.
      onOpenChange={(_ev, data) => {
        if (!data.open && !isAssociating) {
          onSkip();
        }
      }}
      modalType="modal"
    >
      <DialogSurface data-testid="create-on-save-association-gate">
        <DialogBody>
          <DialogTitle>Link this document (optional)</DialogTitle>
          <DialogContent className={styles.content}>
            {error ? (
              <MessageBar intent="warning" data-testid="association-gate-error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            ) : null}

            <CreateOnSaveAssociationPrompt
              navigationService={navigationService}
              value={association}
              onChange={onChange}
              disabled={isAssociating}
              {...(allowedTargets ? { allowedTargets } : {})}
            />
          </DialogContent>
          <DialogActions>
            <Button
              appearance="secondary"
              onClick={onSkip}
              disabled={isAssociating}
              data-testid="association-gate-skip"
            >
              Skip
            </Button>
            <Button
              appearance="primary"
              onClick={onConfirm}
              disabled={isAssociating}
              icon={isAssociating ? <Spinner size="tiny" /> : undefined}
              data-testid="association-gate-confirm"
            >
              Done
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

export default CreateOnSaveAssociationGateDialog;
