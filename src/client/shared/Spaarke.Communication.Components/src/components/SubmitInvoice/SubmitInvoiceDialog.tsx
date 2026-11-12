/**
 * SubmitInvoiceDialog - hosts {@link SubmitInvoiceFromEmail} in the standard modal shell (ebilling-r1 task 118).
 *
 * The toolbar button, a flagged attachment's "Submit as invoice" and the CommunicationActions PCF's button all open
 * the flow in a modal; the Email Review dialog hosts the same flow inline in its "Invoice" tab instead. Composes the
 * ADR-050 `SprkModal` base shell (no hand-rolled Dialog); explicit dismiss, so an accidental ESC or backdrop click
 * cannot lose what was entered. The close button is held while a submit request is in flight, so the answer
 * (accepted or refused) is never lost with the dialog.
 */
import * as React from 'react';
import { SprkModal } from '@spaarke/ui-components';
import { SubmitInvoiceFromEmail, type SubmitInvoiceFromEmailProps } from './SubmitInvoiceFromEmail';

export interface SubmitInvoiceDialogProps extends Omit<SubmitInvoiceFromEmailProps, 'onClose' | 'onBusyChange'> {
  open: boolean;
  onClose: () => void;
}

export const SubmitInvoiceDialog: React.FC<SubmitInvoiceDialogProps> = ({ open, onClose, ...flow }) => {
  const [busy, setBusy] = React.useState(false);
  const handleClose = React.useCallback((): void => {
    if (!busy) onClose();
  }, [busy, onClose]);

  return (
    <SprkModal open={open} onClose={handleClose} title="Submit Invoice" size="md" dismiss="explicit">
      <SubmitInvoiceFromEmail {...flow} onClose={handleClose} onBusyChange={setBusy} />
    </SprkModal>
  );
};

SubmitInvoiceDialog.displayName = 'SubmitInvoiceDialog';
