import * as React from 'react';
import { Button, Spinner, makeStyles, tokens } from '@fluentui/react-components';
import { SprkModal, type SprkModalBodyScroll } from '../SprkModal';

/**
 * FormModal — thin `SprkModal` config for light-edit forms (spec FR-09; design
 * §6.1/§6.8). Supplies the standard Cancel (left) / Save (right) footer driven
 * by `onClose`/`onSubmit`, defaults to `md`, and uses `explicit` dismiss (forms
 * don't light-dismiss); active-compose surfaces may escalate to `alert`
 * (FR-14/FR-05 — no ESC/backdrop loss of an in-progress draft). The consumer
 * supplies only the fields — this preset owns NO Dialog/header/footer of its
 * own. `submitDisabled` (validation) and `busy` (in-flight submit: both
 * buttons disabled + spinner on Save, mirroring `ConfirmModal.busy`) were
 * added in the P3 consolidation so real forms fit the literal preset.
 */
const useStyles = makeStyles({
  formBody: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
});

export interface FormModalProps {
  /** Whether the modal is open. */
  open: boolean;
  /** Close callback — wired to the × and the Cancel button. */
  onClose: () => void;
  /** Submit callback — wired to the primary Save button. */
  onSubmit: () => void;
  /** Header title (ellipsized; announced). */
  title: string;
  /** Named size — `sm` or `md` (default `md`). */
  size?: 'sm' | 'md';
  /** Primary button label (default "Save"). */
  submitLabel?: string;
  /** Cancel button label (default "Cancel") — mirrors the sibling presets. */
  cancelLabel?: string;
  /** Disables Save only (validation-gated submit); Cancel stays available. */
  submitDisabled?: boolean;
  /**
   * In-flight submit: disables BOTH buttons and shows a spinner on Save
   * (prevents double-submit; the × routes to `onClose`, so hosts should keep
   * their `onClose` no-op while busy). Pair with a `submitLabel` swap as desired.
   */
  busy?: boolean;
  /**
   * Dismiss semantics (default `explicit` — forms never light-dismiss).
   * Active-compose surfaces (e.g. the email compose dialog, FR-14/FR-05) use
   * `alert` so ESC/backdrop cannot discard an in-progress draft.
   */
  dismiss?: 'explicit' | 'alert';
  /** The `--sprk-ui-scale` factor for sizing, forwarded to the shell. */
  uiScale?: number;
  /** Body scroll mode, forwarded to the shell (default `native`). */
  bodyScroll?: SprkModalBodyScroll;
  /** Form fields. */
  children: React.ReactNode;
}

export const FormModal: React.FC<FormModalProps> = ({
  open,
  onClose,
  onSubmit,
  title,
  size = 'md',
  submitLabel = 'Save',
  cancelLabel = 'Cancel',
  submitDisabled,
  busy,
  dismiss = 'explicit',
  uiScale,
  bodyScroll,
  children,
}) => {
  const styles = useStyles();
  return (
    <SprkModal
      open={open}
      onClose={onClose}
      title={title}
      size={size}
      dismiss={dismiss}
      uiScale={uiScale}
      bodyScroll={bodyScroll}
      footerStart={
        <Button appearance="secondary" onClick={onClose} disabled={busy}>
          {cancelLabel}
        </Button>
      }
      footer={
        <Button
          appearance="primary"
          onClick={onSubmit}
          disabled={busy || submitDisabled}
          icon={busy ? <Spinner size="tiny" /> : undefined}
        >
          {submitLabel}
        </Button>
      }
    >
      <div className={styles.formBody}>{children}</div>
    </SprkModal>
  );
};

export default FormModal;
