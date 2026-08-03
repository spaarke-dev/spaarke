/**
 * useEmailComposeActions.tsx
 *
 * Builds the reading-pane toolbar's real dispatch (email-communication-
 * solution-r5 task 036, FR-09/FR-10/FR-15) on top of:
 *   - the task-022 extracted Layer-1 logic (`deriveComposerFields` —
 *     `@spaarke/communication-components/logic/actions`), and
 *   - the CANONICAL `SendEmailDialog`/`EmailComposer`
 *     (`@spaarke/ui-components`) — mounted here VERBATIM, never forked.
 *
 * Reply / Reply All / Forward / New each open the ONE `SendEmailDialog`
 * instance this hook owns, in the correct engine mode, with recipients
 * pre-filled per mode:
 *   - reply     → sender only in `initialTo`
 *   - replyAll  → sender in `initialTo`, everyone else (To+Cc, deduped,
 *                 sender removed) in `initialCc`
 *   - forward   → empty recipients + quoted original body
 *   - new       → fully empty compose (no record read at all)
 *
 * Send flows through the EXISTING BFF path INSIDE the composer (no new
 * send/compose endpoint here — `sendCommunication()` owns that, see
 * `communicationApi.ts`); `onSent` is the host's seam to refresh the reading
 * pane. "Open full form" (FR-15) opens the OOB `sprk_communication` Email
 * main form as an 85% `navigateTo` modal via `INavigationService.openRecordModal`
 * (`docs/standards/MODAL-DECISION-CRITERIA.md`).
 *
 * React-version note (ADR-022/NFR-05): standard hooks only, no
 * `as React.ComponentType` cast — this is a React-19 Layer-2 module (the
 * canonical `SendEmailDialog` is authored against React 19 types already;
 * unlike the PCF's `CommunicationActionsApp`, no React-16 boundary cast is
 * needed here).
 */
import * as React from 'react';
import { SendEmailDialog } from '@spaarke/ui-components';
import type { IAttachmentItem } from '@spaarke/ui-components';
import {
  deriveComposerFields,
  fetchSourceAttachments,
  type ComposerMode,
  type RecordPrefill,
  type IActionsWebApi,
  type ISourceAttachmentRecord,
} from '../../logic/actions';
import { fetchCommunicationPrefill } from './fetchCommunicationPrefill';
import type { EmailComposeActionsDeps, UseEmailComposeActionsResult } from './EmailComposeActions.types';

interface DialogState {
  mode: ComposerMode;
  /** Undefined for `mode === 'compose'` (New — not record-scoped). */
  communicationId?: string;
  /** `null` while the record read is in flight (or failed) — deriveComposerFields treats `null` as "no data yet". */
  prefill: RecordPrefill | null;
  /** Source-communication attachments carried onto reply/replyAll/forward (empty for compose). */
  initialAttachments?: IAttachmentItem[];
}

/**
 * Builds the toolbar action handlers + the composer dialog element that
 * implements them. Call once per reading-pane instance; render
 * `composerDialog` once, anywhere below the host's `FluentProvider`.
 */
export function useEmailComposeActions(deps: EmailComposeActionsDeps): UseEmailComposeActionsResult {
  const {
    authenticatedFetch,
    bffBaseUrl,
    dataService,
    navigationService,
    onSearchRecipients,
    onLookupRecipients,
    recordLookupCatalog,
    onLookupRecord,
    onAddRelationship,
    onUploadLocalAttachment,
    onResolveShareLink,
    onListEmailTemplates,
    onRenderEmailTemplate,
    onDraftWithAi,
    aiDraftActions,
    fromMailbox,
    dataverseUrl,
    associations,
    onSent,
    onError,
    onClose,
  } = deps;

  const [dialogState, setDialogState] = React.useState<DialogState | null>(null);
  // Guards against a stale prefill read landing after the user re-triggered a
  // different (or repeated) compose action before the first read settled.
  const requestIdRef = React.useRef(0);

  const openComposer = React.useCallback(
    (mode: ComposerMode, communicationId?: string) => {
      const requestId = ++requestIdRef.current;
      if (!communicationId) {
        // New — not record-scoped, nothing to read; open immediately, fully empty.
        setDialogState({ mode, communicationId: undefined, prefill: null });
        return;
      }
      // Load the source record BEFORE opening the dialog. `initial*` props seed the
      // canonical composer's OWN internal state ONCE at mount (uncontrolled-style) —
      // opening first and patching `initialTo`/`initialSubject`/`initialBody` in on a
      // later render would NOT re-seed an already-mounted composer. Loading first (not
      // "open blank, then fix in place") is what makes Reply/Reply All/Forward open
      // with the correct fields already populated.
      void (async () => {
        // Read prefill (Re:/Fwd: fields) and the parent's carry-forward
        // attachments in parallel — mirrors CommunicationActionsApp, which
        // enumerates both from the source record. Both are best-effort:
        // fetchCommunicationPrefill may throw (caught → empty pre-fill);
        // fetchSourceAttachments never throws (returns []). The
        // `IDataService.retrieveMultipleRecords` shape satisfies the narrower
        // `IActionsWebApi` the enumerator needs.
        const [prefill, initialAttachments] = await Promise.all([
          fetchCommunicationPrefill(dataService, communicationId).catch((err: unknown) => {
            console.warn('[EmailComposeActions] source record prefill read failed:', err);
            return null;
          }),
          fetchSourceAttachments(
            {
              retrieveMultipleRecords: (entity, options) =>
                dataService.retrieveMultipleRecords(entity, options) as Promise<{
                  entities: ISourceAttachmentRecord[];
                }>,
            } as IActionsWebApi,
            communicationId,
            dataverseUrl ?? ''
          ),
        ]);
        if (requestIdRef.current !== requestId) return; // superseded by a newer action
        setDialogState({
          mode,
          communicationId,
          prefill,
          initialAttachments: initialAttachments.length > 0 ? initialAttachments : undefined,
        });
      })();
    },
    [dataService, dataverseUrl]
  );

  const handleClose = React.useCallback(() => {
    setDialogState(null);
    // Standalone hosts (the sprk_emailpage compose mode) close their window here;
    // the reading-pane host omits `onClose` and the dialog just unmounts in place.
    onClose?.();
  }, [onClose]);

  const actions = React.useMemo(
    () => ({
      onReply: (selectedId: string) => openComposer('reply', selectedId),
      onReplyAll: (selectedId: string) => openComposer('replyAll', selectedId),
      onForward: (selectedId: string) => openComposer('forward', selectedId),
      onNew: () => openComposer('compose'),
    }),
    [openComposer]
  );

  const openFullForm = React.useCallback(
    async (communicationId: string) => {
      if (!navigationService.openRecordModal) {
        console.warn(
          '[EmailComposeActions] "Open full form" is unavailable — the host navigationService does not implement openRecordModal.'
        );
        return;
      }
      await navigationService.openRecordModal('sprk_communication', communicationId);
    },
    [navigationService]
  );

  const isOpen = dialogState !== null;
  const activeMode = dialogState?.mode ?? 'compose';
  // Reply All reuses the engine's 'reply' mode (shared Re:/reply chrome) with the
  // wider recipient set carried entirely via initialTo/initialCc — mirrors
  // CommunicationActionsApp's `sendMode` mapping exactly (no engine change needed).
  const engineMode = activeMode === 'replyAll' ? 'reply' : activeMode;
  const isRecordScoped = dialogState !== null && dialogState.mode !== 'compose';
  const composerFields = dialogState ? deriveComposerFields(dialogState.mode, dialogState.prefill) : {};

  // Header title override — "Reply: <subject>" / "Reply All: <subject>" /
  // "Forward: <subject>" for the record-scoped modes (New keeps the engine's
  // default 'New Email'). Falls back to the engine default when there's no
  // subject yet.
  const titleOverride = React.useMemo<string | undefined>(() => {
    if (!dialogState) return undefined;
    const subject = dialogState.prefill?.subject?.trim();
    if (!subject) return undefined;
    const label =
      dialogState.mode === 'reply'
        ? 'Reply'
        : dialogState.mode === 'replyAll'
          ? 'Reply All'
          : dialogState.mode === 'forward'
            ? 'Forward'
            : undefined;
    return label ? `${label}: ${subject}` : undefined;
  }, [dialogState]);

  const composerDialog = (
    <SendEmailDialog
      open={isOpen}
      onClose={handleClose}
      // UAT 2026-08-02: Reply/ReplyAll/Forward/New open while the OOB email-record
      // modal is already on screen — render the composer full-bleed so it fully
      // covers the underlying modal instead of floating inside it.
      fullBleed
      mode={engineMode}
      communicationId={isRecordScoped ? dialogState?.communicationId : undefined}
      authenticatedFetch={authenticatedFetch}
      bffBaseUrl={bffBaseUrl}
      onSearchRecipients={onSearchRecipients}
      onLookupRecipients={onLookupRecipients}
      recordLookupCatalog={recordLookupCatalog}
      onLookupRecord={onLookupRecord}
      onAddRelationship={onAddRelationship}
      onUploadLocalAttachment={onUploadLocalAttachment}
      onResolveShareLink={onResolveShareLink}
      onListEmailTemplates={onListEmailTemplates}
      onRenderEmailTemplate={onRenderEmailTemplate}
      onDraftWithAi={onDraftWithAi}
      aiDraftActions={aiDraftActions}
      defaultSendMode="user"
      fromMailbox={fromMailbox}
      associations={isRecordScoped ? associations : undefined}
      initialAttachments={isRecordScoped ? dialogState?.initialAttachments : undefined}
      titleOverride={titleOverride}
      onSent={onSent}
      onError={onError}
      {...composerFields}
    />
  );

  return { actions, composerDialog, openFullForm };
}

export type { UseEmailComposeActionsResult };
