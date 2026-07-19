/**
 * SendEmailDialog.tsx — semantic wrapper for the popup caller (task 021, FR-12).
 *
 * Locks `mount='dialog'` and owns the Fluent `Dialog` open/close lifecycle. The
 * engine renders its own header + action bar for the `dialog` mount, so this
 * wrapper only supplies the `Dialog` chrome and maps callbacks:
 *   - engine `onSent(result)` → `props.onSent?.(communicationId)` then `onClose()`
 *   - engine `onCancel`       → `onClose`
 *   - Dialog dismiss (Esc / backdrop) → `onClose`
 *
 * This is THE canonical dialog wrapper (design §5.1.1) — it supersedes the legacy
 * single-caller `components/SendEmailDialog`; the W6 FilePreviewDialog migration
 * (task 060) consumes it. Thin by contract (ADR-045): no business logic.
 *
 * No `@spaarke/auth` import (ADR-028) — `authenticatedFetch` is injected via props.
 */
import * as React from 'react';
import { Dialog, DialogSurface, DialogBody } from '@fluentui/react-components';

import { EmailComposer } from '../EmailComposer';
import type { EmailComposerMode, EmailComposerBodyFormat, IComposerAttachmentSource } from '../EmailComposer.types';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import type { ICommunicationAssociation, SendCommunicationError } from '../../../services/communicationApi';
import type { ILookupItem } from '../../../types/LookupTypes';

export interface ISendEmailDialogProps {
  open: boolean;
  onClose: () => void;
  /** Defaults to `'compose'`. */
  mode?: EmailComposerMode;
  initialTo?: string[];
  initialSubject?: string;
  /** Pre-fill the composer body (e.g. a document summary). Forwarded to the engine. */
  initialBody?: string;
  /** Body format for the pre-filled body. Defaults to `'HTML'` in the engine — pass `'PlainText'` for `\n`-delimited plain-text templates. */
  initialBodyFormat?: EmailComposerBodyFormat;
  associations?: ICommunicationAssociation[];
  attachmentSources?: IComposerAttachmentSource[];
  /** Recipient directory lookup, forwarded to the engine's `RecipientField`. */
  onSearchRecipients?: (query: string) => Promise<ILookupItem[]>;
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl: string;
  onSent?: (communicationId: string) => void;
  onError?: (err: SendCommunicationError) => void;
}

export function SendEmailDialog(props: ISendEmailDialogProps) {
  const { open, onClose, mode, onSent, onError, ...composerProps } = props;

  return (
    <Dialog
      open={open}
      onOpenChange={(_event, data) => {
        if (!data.open) onClose();
      }}
    >
      <DialogSurface>
        <DialogBody>
          <EmailComposer
            {...composerProps}
            mount="dialog"
            mode={mode ?? 'compose'}
            onSent={result => {
              onSent?.(result.communicationId);
              onClose();
            }}
            onCancel={onClose}
            onError={onError}
          />
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
