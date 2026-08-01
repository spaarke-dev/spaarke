import * as React from 'react';
import { Button, makeStyles, tokens } from '@fluentui/react-components';
import { SprkModal, type SprkModalBodyScroll } from '../SprkModal';

/**
 * FormModal — thin `SprkModal` config for light-edit forms (spec FR-09; design
 * §6.1/§6.8). Supplies the standard Cancel (left) / Save (right) footer driven
 * by `onClose`/`onSubmit`, defaults to `md`, and uses `explicit` dismiss (forms
 * don't light-dismiss). The consumer supplies only the fields — this preset
 * owns NO Dialog/header/footer of its own.
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
      dismiss="explicit"
      uiScale={uiScale}
      bodyScroll={bodyScroll}
      footerStart={
        <Button appearance="secondary" onClick={onClose}>
          Cancel
        </Button>
      }
      footer={
        <Button appearance="primary" onClick={onSubmit}>
          {submitLabel}
        </Button>
      }
    >
      <div className={styles.formBody}>{children}</div>
    </SprkModal>
  );
};

export default FormModal;
