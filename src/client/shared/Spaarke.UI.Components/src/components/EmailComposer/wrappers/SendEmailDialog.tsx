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
import { Dialog, DialogSurface, DialogBody, makeStyles, mergeClasses } from '@fluentui/react-components';
import { SIZE_SPEC } from '../../SprkModal/sizes';

import { EmailComposer } from '../EmailComposer';
import type {
  EmailComposerMode,
  EmailComposerBodyFormat,
  IComposerAttachmentSource,
  IComposerRecordLink,
  ISourceCommunicationRecord,
  IAttachmentItem,
  IRecordLookupTarget,
  IPickedRecord,
  IRecipient,
  IEmailTemplateSummary,
  IEmailTemplateRenderResult,
  IEmailAiDraftAction,
  IEmailAiDraftRequest,
  IEmailAiDraftResult,
} from '../EmailComposer.types';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import type {
  ICommunicationAssociation,
  SendCommunicationError,
  CommunicationSendMode,
} from '../../../services/communicationApi';
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

// Standard Spaarke mid-size modal rectangle (owner UAT 2026-07-22): 720px × 70vh — the recurring
// mid-size across ~6 Spaarke dialogs (QuickStartModal, daily-update email dialog, ContainerTypeConfig).
// Distinct from the two MODAL-DECISION-CRITERIA layouts (OOB 85%×85% / content-driven preview): the
// canonical compose/read surface is a fixed mid-size rectangle, not full-height. The surface owns the
// bounded height; `body` fills it and scrolls internally so the composer content (attachments + body)
// scrolls while the dialog stays a clean rectangle.
const useDialogStyles = makeStyles({
  surface: {
    // Landscape mid-size rectangle (owner UAT 2026-07-22 #1): the 720px width read as
    // portrait — widen so the composer is a wider-than-tall rectangle like the mockup.
    maxWidth: '1040px',
    width: '92vw',
    height: '72vh',
    // spaarke-modal-system P3 (FR-14 alignment, 2026-08-02): cap the 72vh height at
    // the `md` heightMax so this surface is numerically identical to the SprkModal
    // `md` size (`min(1040px, 92vw) × min(72vh, 720px)`) — the cap fixes the
    // square-on-tall-monitor failure `md` exists for. Sourced from SIZE_SPEC (not a
    // literal) so a future `md` retune propagates here (task-100 gate fix). The
    // literal FormModal re-base is deferred: EmailComposer is self-chromed (see
    // notes/task-051-completion.md + DEF-002 / Issue #713).
    maxHeight: `${SIZE_SPEC.md.heightMax}px`,
    display: 'flex',
    flexDirection: 'column',
    // Surface itself never scrolls — the body owns the scroll region below.
    overflow: 'hidden',
  },
  // Maximized (owner UAT 2026-07-30, item 11): fill the app container / mounted surface —
  // NOT the physical screen. `100%` resolves against the Dialog's positioning context (the
  // viewport/host surface the dialog is mounted in); the `maxWidth` cap is lifted so the
  // surface can span the full width. Toggled back to the default rectangle on restore.
  surfaceMaximized: {
    maxWidth: '100%',
    width: '100%',
    height: '100%',
  },
  body: {
    display: 'flex',
    flexDirection: 'column',
    // The composer now owns the scroll region (pinned header/footer + scrollable body,
    // owner UAT round 3), so the dialog body itself never scrolls.
    minHeight: 0,
    flexGrow: 1,
    overflow: 'hidden',
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
  /**
   * Source-communication attachments offered for inclusion on reply/replyAll/
   * forward. Forwarded verbatim to the engine (which applies the per-mode
   * attach-file default). Additive/optional — behaviorally identical to the
   * rich `SendEmailPage` wrapper's prop of the same name; omitted → no carried
   * attachments (today's dialog behavior).
   */
  initialAttachments?: IAttachmentItem[];
  /** Recipient directory lookup, forwarded to the engine's `RecipientField`. */
  onSearchRecipients?: (query: string) => Promise<ILookupItem[]>;
  /**
   * Advanced OOB recipient lookup — clicking a To/Cc/Bcc label box opens the
   * host people picker and appends resolved recipients. Additive/optional
   * (mirrors `ISendEmailPageProps.onLookupRecipients`); omitted → the fields
   * stay free-text/typeahead only.
   */
  onLookupRecipients?: (field: 'to' | 'cc' | 'bcc') => Promise<IRecipient[] | null>;
  /**
   * Record-lookup targets + host lookup for the body-toolbar "link a document" /
   * "insert a link to a record" tools. Additive/optional (mirror
   * `ISendEmailPageProps`); omitted → those toolbar tools are not shown.
   */
  recordLookupCatalog?: IRecordLookupTarget[];
  onLookupRecord?: (entityType: string) => Promise<IPickedRecord | null>;
  /**
   * Add-a-relationship (connector toolbar icon). Additive/optional (mirrors
   * `ISendEmailPageProps.onAddRelationship`); omitted → the connector tool is
   * not shown.
   */
  onAddRelationship?: () => Promise<IPickedRecord | null>;
  /**
   * Compose template picker (Wave E). `onListEmailTemplates` lists selectable OOB
   * `template` records; `onRenderEmailTemplate` renders the chosen one (merging field
   * codes from the primary regarding). Both forwarded to the engine via `...composerProps`;
   * omit either → the toolbar template button is hidden.
   */
  onListEmailTemplates?: () => Promise<IEmailTemplateSummary[]>;
  onRenderEmailTemplate?: (args: {
    templateId: string;
    regardingEntityType?: string;
    regardingRecordId?: string;
  }) => Promise<IEmailTemplateRenderResult>;
  /**
   * AI "sparkle" draft (Wave E). `onDraftWithAi` generates/refines the body via the BFF; optional
   * `aiDraftActions` overrides the preset quick-actions. Both forwarded to the engine via
   * `...composerProps`; omit `onDraftWithAi` → the sparkle button is hidden.
   */
  onDraftWithAi?: (req: IEmailAiDraftRequest) => Promise<IEmailAiDraftResult>;
  aiDraftActions?: IEmailAiDraftAction[];
  /**
   * Resolve a locally-picked file to a governed `sprk_document` (item 9b) so it
   * flows into the send payload. Forwarded verbatim to the engine via the
   * `...composerProps` spread; omitted → local picks stay display-only.
   */
  onUploadLocalAttachment?: (file: File) => Promise<{ documentId: string; driveItemId?: string; linkUrl?: string }>;
  /**
   * Resolve a recipient-openable SPE sharing link for a linked document (R2 item 12). Forwarded to
   * the engine via `...composerProps`; omitted → links keep their original internal URL.
   */
  onResolveShareLink?: (documentId: string) => Promise<string | null>;
  /**
   * Seeds the initial From/send mode while keeping the switcher interactive (item 3).
   * The email surface passes `'user'`. Forwarded to the engine via `...composerProps`.
   */
  defaultSendMode?: CommunicationSendMode;
  /**
   * The signed-in user's mailbox address, shown as the "From: <user>" label when the
   * user option is selected (item 3). Host-resolved (context-agnostic engine).
   */
  fromMailbox?: string;
  /**
   * Optional header-title override forwarded to the engine (e.g.
   * `Reply: <subject>`). Additive/optional; omitted → the engine's mode-derived
   * title ('Reply'/'Forward'/'New Email'/…) is used, unchanged.
   */
  titleOverride?: string;
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

  // Maximize/restore (owner UAT 2026-07-30, item 11). The surface size is owned here (the
  // engine owns the header where the toggle button lives), so the composer receives an
  // `onToggleMaximize` callback + the current `isMaximized` flag and renders the button.
  const [maximized, setMaximized] = React.useState(false);
  // Always restore to the default rectangle whenever the dialog is (re)opened.
  React.useEffect(() => {
    if (!open) setMaximized(false);
  }, [open]);

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
      // modalType="alert" disables light-dismiss (backdrop click) AND Escape-to-close so an
      // accidental click outside can't discard an in-progress draft (owner UAT 2026-07-30,
      // item 12). The dialog now closes ONLY via the explicit X / Cancel affordances, which
      // call onClose directly.
      modalType="alert"
      onOpenChange={(_event, data) => {
        if (!data.open) onClose();
      }}
    >
      <DialogSurface className={mergeClasses(dialogStyles.surface, maximized && dialogStyles.surfaceMaximized)}>
        <DialogBody className={dialogStyles.body}>
          <EmailComposer
            {...composerProps}
            associations={mergedAssociations}
            mount="dialog"
            mode={mode ?? 'compose'}
            isMaximized={maximized}
            onToggleMaximize={() => setMaximized(m => !m)}
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
