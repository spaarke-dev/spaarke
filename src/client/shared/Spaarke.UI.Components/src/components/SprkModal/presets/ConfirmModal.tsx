import * as React from 'react';
import { Button, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { SprkModal } from '../SprkModal';

/**
 * ConfirmModal — a thin `SprkModal` config for a yes/no confirmation (spec FR-09).
 * `size="xs"`, `dismiss="alert"` (no ESC/backdrop light-dismiss), non-maximizable;
 * Cancel in `footerStart` (left), Confirm in `footer` (right). The optional
 * `destructive` variant applies danger styling to Confirm via a `makeStyles` TOKEN
 * class (`tokens.colorStatusDangerBackground3` + `filter: brightness(...)` on
 * hover/active) — never an inline color style (ADR-021). `busy` disables both
 * buttons and shows a spinner on Confirm while an async confirm is in flight
 * (P2 consolidation — restores the disabled/spinner parity the retired
 * `ActionConfirmationDialog` overlay had, task 042/040 findings).
 *
 * Ported verbatim from the prototype `presets.tsx` `ConfirmModal` (design §6.1).
 */
const useStyles = makeStyles({
  danger: {
    backgroundColor: tokens.colorStatusDangerBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
    ':hover': {
      backgroundColor: tokens.colorStatusDangerBackground3,
      color: tokens.colorNeutralForegroundOnBrand,
      filter: 'brightness(0.94)',
    },
    ':hover:active': {
      backgroundColor: tokens.colorStatusDangerBackground3,
      color: tokens.colorNeutralForegroundOnBrand,
      filter: 'brightness(0.88)',
    },
  },
});

/**
 * The confirm-family destructive-primary TOKEN class (ADR-021), exported for the
 * dialogs that legitimately compose `SprkModal` directly because their footer
 * shape exceeds `ConfirmModalProps` (3-way conflict choices, phase-dependent
 * footers). Import this instead of copying the recipe so the danger treatment
 * cannot drift from the preset (P2 consolidation, task 040 finding).
 */
export const useDangerButtonClassName = (): string => useStyles().danger;

export interface ConfirmModalProps {
  /** Whether the modal is open. */
  open: boolean;
  /** Close callback — invoked by Cancel and the × (no light-dismiss; `alert`). */
  onClose: () => void;
  /** Invoked when the user confirms. */
  onConfirm: () => void;
  /** Header title. */
  title: string;
  /** Body message. */
  message: React.ReactNode;
  /** Confirm button label (default `"Confirm"`). */
  confirmLabel?: string;
  /** Cancel button label (default `"Cancel"`). */
  cancelLabel?: string;
  /** Applies danger styling to the Confirm button via a token `makeStyles` class. */
  destructive?: boolean;
  /**
   * Disables both buttons and shows a spinner on Confirm while an async confirm
   * is in flight (prevents double-submit; the × also routes to `onClose`, so
   * hosts should keep their `onClose` no-op while busy). Pair with a
   * `confirmLabel` swap (e.g. `"Deleting…"`) as desired.
   */
  busy?: boolean;
  /** The `--sprk-ui-scale` factor for sizing (default 1). */
  uiScale?: number;
}

export const ConfirmModal: React.FC<ConfirmModalProps> = ({
  open,
  onClose,
  onConfirm,
  title,
  message,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  destructive,
  busy,
  uiScale,
}) => {
  const styles = useStyles();
  return (
    <SprkModal
      open={open}
      onClose={onClose}
      title={title}
      size="xs"
      dismiss="alert"
      maximizable={false}
      uiScale={uiScale}
      footerStart={
        <Button appearance="secondary" onClick={onClose} disabled={busy}>
          {cancelLabel}
        </Button>
      }
      footer={
        <Button
          appearance="primary"
          className={destructive ? styles.danger : undefined}
          onClick={onConfirm}
          disabled={busy}
          icon={busy ? <Spinner size="tiny" /> : undefined}
        >
          {confirmLabel}
        </Button>
      }
    >
      <Text>{message}</Text>
    </SprkModal>
  );
};

export default ConfirmModal;
