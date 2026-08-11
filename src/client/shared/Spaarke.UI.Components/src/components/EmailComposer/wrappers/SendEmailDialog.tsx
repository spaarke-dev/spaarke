/**
 * SendEmailDialog.tsx — semantic wrapper for the popup caller (task 021, FR-12).
 *
 * #713 re-base (spaarke-modal-system follow-up, 2026-08-03): the wrapper now
 * composes the canonical `SprkModal` shell instead of a bespoke Fluent
 * `Dialog`/`DialogSurface` envelope. The shell owns ALL window chrome — title,
 * maximize/restore (`size='md'` ↔ `full`), close ×, `alert` dismiss — and the
 * engine mounts with `hostOwnsChrome` so it suppresses its own header (the
 * pre-seam source of double chrome). The `ComposerActionBar` stays in the body
 * (Messages-shape: smart body, dumb frame). Callback mapping unchanged:
 *   - engine `onSent(result)` → `props.onSent?.(communicationId)` then `onClose()`
 *   - engine `onCancel` / shell × → `onClose`
 *   - `alert` dismiss: no Esc/backdrop close (owner UAT 2026-07-30 item 12)
 *
 * This is THE canonical dialog wrapper (design §5.1.1) — it supersedes the legacy
 * single-caller `components/SendEmailDialog`; the W6 FilePreviewDialog migration
 * (task 060) consumes it. Thin by contract (ADR-045): no business logic.
 *
 * No `@spaarke/auth` import (ADR-028) — `authenticatedFetch` is injected via props.
 */
import * as React from 'react';
import { SprkModal } from '../../SprkModal';

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

// Surface geometry now comes ENTIRELY from the SprkModal size scale: `md` is
// `min(1040px·scale, 92vw) × min(72vh, 720px·scale)` — numerically identical to
// the rectangle this wrapper used to hand-roll (owner UAT 2026-07-22 + the P3
// FR-14 alignment) — and maximize/full is the shell's own true-takeover state
// (100vw × 100vh, radius 0), which supersedes the hand-patched surfaceMaximized
// variant from UAT 2026-08-03. No local surface styles remain by design.

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
  /**
   * D-5 fix (spaarkeai-assistant-enhancements-r3 task 025): the quoted-thread
   * block for a reply/forward opened WITHOUT `sourceRecord` (e.g.
   * `useEmailComposeActions`), so the engine's `state.quotedThread` is seeded and
   * the in-dialog AI sparkle re-append preserves it. Forwarded to the engine
   * unchanged via `...composerProps`. See `IEmailComposerProps.initialQuotedThread`.
   */
  initialQuotedThread?: string;
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
  /**
   * UAT 2026-08-02: render at the maximized geometry (100% of the host
   * viewport) from the start, with the maximize toggle hidden. Used by hosts
   * that open the composer while ANOTHER modal is already on screen (e.g.
   * Reply/Forward from the OOB email-record modal) so the composer fully
   * covers the underlying modal instead of floating inside it. Additive;
   * omitted → the default mid-size rectangle + toggle, unchanged.
   * (#713 re-base: implemented as the shell's `full` size with the maximize
   * toggle disabled — same visual contract as before.)
   */
  fullBleed?: boolean;
  /**
   * #713 re-base: the `--sprk-ui-scale` factor forwarded to the shell so the
   * composer surface scales in lock-step with app-shell "Display size"
   * (SpaarkeAi `useUiScale()` hosts). Omitted → 1 (unchanged for all callers).
   */
  uiScale?: number;
}

export function SendEmailDialog(props: ISendEmailDialogProps) {
  const { open, onClose, mode, onSent, onError, regarding, associations, fullBleed, uiScale, ...composerProps } = props;

  // Shell title: mirrors the engine's mode-derived header word (which the seam
  // suppresses), honoring the same `titleOverride` hosts already pass (e.g.
  // `Reply All: <subject>` from useEmailComposeActions — reply-all arrives as
  // engine mode 'reply' + an override, so no reply-all branch exists here).
  const effectiveMode = mode ?? 'compose';
  const title =
    props.titleOverride ??
    (effectiveMode === 'view'
      ? 'Email'
      : effectiveMode === 'reply'
        ? 'Reply'
        : effectiveMode === 'forward'
          ? 'Forward'
          : effectiveMode === 'draft'
            ? 'Edit Draft'
            : 'New Email');

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
    <SprkModal
      open={open}
      onClose={onClose}
      title={title}
      // fullBleed hosts (record-modal reply, dedicated compose window) start —
      // and stay — at the shell's true-takeover `full` size; everyone else gets
      // the standard `md` rectangle with the shell's own maximize toggle.
      size={fullBleed ? 'full' : 'md'}
      maximizable={!fullBleed}
      // `alert`: no Esc/backdrop dismiss — an accidental click outside can't
      // discard an in-progress draft (owner UAT 2026-07-30, item 12). Close is
      // the shell × or the ComposerActionBar Cancel, both routed to onClose.
      dismiss="alert"
      // The composer owns its internal padding + single scroll region.
      padded={false}
      uiScale={uiScale}
    >
      <EmailComposer
        {...composerProps}
        associations={mergedAssociations}
        mount="dialog"
        mode={effectiveMode}
        // The shell draws title + window controls — suppress the engine's header.
        hostOwnsChrome
        onSent={result => {
          onSent?.(result.communicationId);
          onClose();
        }}
        onCancel={onClose}
        onError={onError}
      />
    </SprkModal>
  );
}
