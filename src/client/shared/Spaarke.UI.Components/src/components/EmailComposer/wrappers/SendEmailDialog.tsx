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
import { Dialog, DialogSurface, DialogBody, makeStyles } from '@fluentui/react-components';

import { EmailComposer } from '../EmailComposer';
import type {
  EmailComposerMode,
  EmailComposerBodyFormat,
  IComposerAttachmentSource,
  IComposerRecordLink,
  ISourceCommunicationRecord,
} from '../EmailComposer.types';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import type { ICommunicationAssociation, SendCommunicationError } from '../../../services/communicationApi';
import type { ILookupItem } from '../../../types/LookupTypes';

/** Regarding record to auto-associate the sent email with (R3 task 020, FR-07). */
export interface ISendEmailDialogRegarding {
  /** Dataverse logical name, one of the ADR-024 regarding family (e.g. `sprk_matter`). */
  entityType: string;
  /** Regarding record GUID. */
  id: string;
  /** Optional display name for the association chip. */
  name?: string;
}

// R6-4 (UAT 2026-07-21): Fluent's default DialogSurface caps at ~600px, which made the Assistant's
// email modal read smaller than the standard Spaarke email surface. Widen to 760px to match the
// engine's `dialog` mount cap (there is no shared modal-width token — see MODAL-DECISION-CRITERIA).
const useDialogStyles = makeStyles({
  surface: {
    maxWidth: '760px',
    width: '92vw',
  },
});

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

  // — Conversation context (R3 task 020, FR-07/FR-19) — all optional/additive —
  /**
   * Active conversation thread id. Forwarded to the engine and carried into the
   * send payload so the backend pins the sent email to this thread (FR-19).
   */
  threadId?: string;
  /**
   * Regarding record to AUTO-ASSOCIATE the email with. Folded into the engine's
   * EXISTING `associations` mechanism (ADR-024) — NOT a second association path:
   * it becomes one more `ICommunicationAssociation` that renders as an
   * `AssociationChips` tag and rides the send payload unchanged. Captured when
   * the composer mounts (i.e. on open); changing it on an already-open dialog is
   * not re-seeded — open the dialog with the intended record.
   */
  regarding?: ISendEmailDialogRegarding;
  /** Optional record-link chip rendered in the composer (FR-07). Forwarded to the engine. */
  recordLink?: IComposerRecordLink;

  // — Source-record prefill (view/reply/forward/draft; R3 task 022, FR-08) — additive/optional —
  /**
   * The existing `sprk_communication` record backing `view`/`reply`/`forward`/
   * `draft` modes. When the host opens the dialog with `mode="forward"` and a
   * `sourceRecord`, the engine derives the forward prefill (`Fwd:` subject,
   * quoted body, source attachments) via its EXISTING `deriveForwardState`
   * (`EmailComposer.reducer.ts`) — NO new forward send path (ADR-045). Reaches
   * `<EmailComposer/>` unchanged via the `...composerProps` spread below.
   * Existing callers that pass no `sourceRecord` are unaffected (compose mode).
   *
   * ⚠️ In `forward` mode the composer derives `associations` from
   * `sourceRecord.associations` and does NOT merge the dialog's `regarding`
   * prop. To keep a forwarded email associated with the active record, the host
   * MUST include the regarding record as an entry in `sourceRecord.associations`
   * (dedup on entityType+entityId); the `regarding` prop alone is ignored in
   * forward mode. (`threadId` DOES survive forward mode — it's a top-level
   * send() prop.)
   */
  sourceRecord?: ISourceCommunicationRecord;
  /**
   * The source communication's id (`sprk_communication` GUID), forwarded to the
   * engine. Optional/additive — the engine also reads it from
   * `sourceRecord.communicationId` when omitted (`EmailComposer.reducer.ts`
   * `initialState`).
   */
  communicationId?: string;
}

export function SendEmailDialog(props: ISendEmailDialogProps) {
  const { open, onClose, mode, onSent, onError, regarding, associations, ...composerProps } = props;
  const dialogStyles = useDialogStyles();

  // Fold `regarding` into the EXISTING association mechanism (ADR-024) — one
  // more `ICommunicationAssociation`, deduped against any explicit associations.
  // No second association path is introduced.
  const mergedAssociations = React.useMemo<ICommunicationAssociation[] | undefined>(() => {
    const base = associations ?? [];
    if (!regarding) return base.length > 0 ? base : undefined;
    // Dedup on (entityType, entityId) with a case-insensitive GUID compare —
    // Dataverse ids can arrive in differing case from different sources.
    const already = base.some(
      a => a.entityType === regarding.entityType && a.entityId.toLowerCase() === regarding.id.toLowerCase()
    );
    if (already) return base;
    return [{ entityType: regarding.entityType, entityId: regarding.id, entityName: regarding.name }, ...base];
  }, [associations, regarding]);

  return (
    <Dialog
      open={open}
      onOpenChange={(_event, data) => {
        if (!data.open) onClose();
      }}
    >
      <DialogSurface className={dialogStyles.surface}>
        <DialogBody>
          <EmailComposer
            {...composerProps}
            associations={mergedAssociations}
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
