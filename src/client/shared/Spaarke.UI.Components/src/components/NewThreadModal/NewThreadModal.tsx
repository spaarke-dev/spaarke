/**
 * NewThreadModal.tsx — create a NEW named, record-anchored conversation thread (R3 UAT 2026-07-23 item 9).
 *
 * A Fluent v9 modal reachable from the `<ConversationWorkspace />` shell's ＋ affordance. Two sections:
 *   1. **New Thread** — a `Name` field + an "Associate to record" picker (the SAME wizard-associate pattern
 *      used across Spaarke: `<AssociateToStep />` = a record-TYPE dropdown → the OOB Dataverse record lookup).
 *   2. **Message** — an OPTIONAL first message (PLAIN text — chat messages are not rich text, so no
 *      format/rich-text buttons).
 *
 * ── CONTRACT ──
 * On submit it creates the thread via `POST /api/communications/threads` (`createRecordThread`), a
 * record-anchored (Open) thread keyed on the picked ADR-024 regarding record — NO participant (contrast the
 * former participant-based Direct-thread create). The caller is resolved server-side and becomes the owner,
 * so the new (empty) thread is immediately visible in the caller's all-mode list. When a first message is
 * supplied it is posted through the EXISTING send engine (`sendCommunication`, ADR-045) stamped with the
 * same regarding association; a send failure is NON-FATAL (the thread exists and is still selected).
 *
 * Context-agnostic (ADR-012): no `Xrm`/`ComponentFramework`. All platform I/O is injected —
 * `authenticatedFetch` (BFF) + `navigationService` (the record lookup; the host binds
 * `createXrmNavigationService()`). No `@spaarke/auth` import (ADR-028). Fluent v9 semantic tokens only;
 * dark mode passes through the host `FluentProvider` (ADR-021).
 *
 * spaarke-modal-system task 050 (FR-14): re-based onto the `FormModal` preset at `size="md"` — the bespoke
 * `Dialog`/`DialogSurface` envelope + the interim P1 `ModalWindowControls` wiring are RETIRED; the standard
 * SprkModal chrome (header/× /maximize + Cancel-left/Save-right footer) now comes from the shell (FormModal
 * is maximizable by default, so the maximize affordance is preserved). Field content, validation, and the
 * create/send contract above are UNCHANGED — this is a frame/wiring re-base only (no content redesign).
 * `authenticatedFetch`/`navigationService` keep flowing through as FUNCTIONS (ADR-028) — the re-base does
 * not touch that contract. Dismiss semantics move from Fluent's default light-dismiss (ESC/backdrop closed
 * the dialog) to `FormModal`'s `explicit` default (no ESC/backdrop dismiss — only × or Cancel) per FR-05
 * ("forms use explicit"); this is an intentional part of adopting the standard chrome, not a regression.
 */
import * as React from 'react';
import {
  Field,
  Input,
  Textarea,
  Text,
  MessageBar,
  MessageBarBody,
  makeStyles,
  tokens,
} from '@fluentui/react-components';

import { FormModal } from '../SprkModal/presets/FormModal';
import { AssociateToStep } from '../AssociateToStep';
import type { AssociationResult, EntityTypeOption } from '../AssociateToStep';
import type { INavigationService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import { sendCommunication, type ICommunicationAssociation } from '../../services/communicationApi';
import { createRecordThread, type IThreadListApiClientOptions } from '../../services/communicationThreadListApi';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Optional record association (ADR-024) to PRE-SEED + LOCK the record picker. The host supplies it from
 * record context (e.g. a Matter-form host); omit it for the workspace (the user picks any record). Mirrors
 * the shape callers already pass to the send dialog.
 */
export interface INewThreadModalRegarding {
  /** Dataverse logical name — one of the ADR-024 regarding family (e.g. `sprk_matter`). */
  entityType: string;
  /** Regarding record GUID. */
  id: string;
  /** Optional display name for the record. */
  name?: string;
}

export interface INewThreadModalProps {
  /** Controls Dialog visibility. */
  open: boolean;
  /** Fired on cancel, Esc/backdrop dismiss, and after a successful create. */
  onDismiss: () => void;
  /** Injected auth (ADR-028) — forwarded to `createRecordThread` + `sendCommunication`. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** Host only, no `/api` — forwarded exactly as the communication API wrappers expect. */
  bffBaseUrl?: string;
  /**
   * Record-lookup service for the "Associate to record" picker (`<AssociateToStep />`). The host binds
   * `createXrmNavigationService()` (Xrm-backed). Context-agnostic — the modal never imports `Xrm`.
   */
  navigationService: INavigationService;
  /**
   * Success callback — the new thread id. The shell wires this to its thread-selection path so the new
   * thread becomes selected. Also fired when the thread was created but its first message failed to send
   * (non-fatal) — the thread exists and must be selectable.
   */
  onThreadCreated: (threadId: string) => void;
  /**
   * Optional record association to PRE-SEED + LOCK the picker (host is record-scoped). When present the
   * record type + record are fixed to this value; when absent the user picks any ADR-024 record.
   */
  regarding?: INewThreadModalRegarding;
  /** Fired on any submit failure — create failure (fatal) or first-message send failure (non-fatal). */
  onError?: (error: Error) => void;
  /** Optional Dialog title override. Default "New conversation". */
  title?: string;
  /**
   * The `--sprk-ui-scale` factor (spec FR-06 / task-020 seam), forwarded to the `FormModal` shell.
   * Optional, backward-compatible passthrough — omit to use the shell's own default (1); hosts that
   * don't yet call `useUiScale()` need no changes.
   */
  uiScale?: number;
}

// ---------------------------------------------------------------------------
// Regarding targets — the 11 ADR-024 communication regarding families (mirrors the
// CommunicationConversationPanel manifest + the by-regarding endpoint's family set).
// ---------------------------------------------------------------------------

const COMMUNICATION_REGARDING_TARGETS: EntityTypeOption[] = [
  { label: 'Matter', entityType: 'sprk_matter' },
  { label: 'Project', entityType: 'sprk_project' },
  { label: 'Invoice', entityType: 'sprk_invoice' },
  { label: 'Service Request', entityType: 'sprk_servicerequest' },
  { label: 'Work Assignment', entityType: 'sprk_workassignment' },
  { label: 'Event', entityType: 'sprk_event' },
  { label: 'Budget', entityType: 'sprk_budget' },
  { label: 'Analysis', entityType: 'sprk_analysis' },
  { label: 'Organization', entityType: 'sprk_organization' },
  { label: 'Account', entityType: 'account' },
  { label: 'Contact', entityType: 'contact' },
];

// ---------------------------------------------------------------------------
// Styles — FormModal/SprkModal now own the envelope (surface size, header,
// maximize, footer); only the FORM CONTENT styles remain here (task 050 re-base).
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // A clearly-delineated section (round 4): a bold header with an underline rule
  // over a subordinate, indented body so the fields read as "under" the section.
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  sectionHeader: {
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    paddingBottom: tokens.spacingVerticalXS,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  sectionBody: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    // Subordinate the fields under their section header.
    paddingLeft: tokens.spacingHorizontalM,
  },
  // The "Send a message" section (round 4) — best-effort fill of the remaining
  // body height under the shared FormModal/SprkModal body wrapper.
  messageSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    flex: '1 1 auto',
    minHeight: 0,
  },
  messageBody: {
    display: 'flex',
    flex: '1 1 auto',
    minHeight: '120px',
    paddingLeft: tokens.spacingHorizontalM,
  },
  // Fluent Textarea — fill the section's available height + width.
  messageTextarea: {
    width: '100%',
    height: '100%',
    '& textarea': {
      height: '100%',
      maxHeight: 'none',
    },
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function toError(err: unknown, fallback: string): Error {
  return err instanceof Error ? err : new Error(fallback);
}

/** Inline notice surfaced to the user (fatal error vs. non-fatal warning). */
interface INotice {
  text: string;
  intent: 'error' | 'warning';
}

/** Seeds the record picker from an optional host-supplied regarding. */
function seedAssociation(regarding?: INewThreadModalRegarding): AssociationResult | null {
  return regarding
    ? { entityType: regarding.entityType, recordId: regarding.id, recordName: regarding.name ?? '' }
    : null;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NewThreadModal: React.FC<INewThreadModalProps> = ({
  open,
  onDismiss,
  authenticatedFetch,
  bffBaseUrl,
  navigationService,
  onThreadCreated,
  regarding,
  onError,
  title = 'New conversation',
  uiScale,
}) => {
  const styles = useStyles();

  const [name, setName] = React.useState('');
  const [association, setAssociation] = React.useState<AssociationResult | null>(() => seedAssociation(regarding));
  const [body, setBody] = React.useState('');
  const [submitting, setSubmitting] = React.useState(false);
  const [associationError, setAssociationError] = React.useState<string | undefined>(undefined);
  const [notice, setNotice] = React.useState<INotice | undefined>(undefined);

  // Re-seed the picker if the host's regarding changes while the modal is open.
  React.useEffect(() => {
    setAssociation(seedAssociation(regarding));
  }, [regarding]);

  // Guard post-await setState — the host owns `open` and may unmount this modal mid-flight (React 16.14
  // PCF target). `onThreadCreated` / `onDismiss` are host callbacks and stay safe; only LOCAL setState is gated.
  const mountedRef = React.useRef(true);
  React.useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const client = React.useMemo<IThreadListApiClientOptions>(
    () => ({ authenticatedFetch, bffBaseUrl }),
    [authenticatedFetch, bffBaseUrl]
  );

  // The picker is locked to the host-supplied record when one was provided (record-scoped host).
  const recordLocked = !!regarding;

  const resetState = React.useCallback(() => {
    setName('');
    setAssociation(seedAssociation(regarding));
    setBody('');
    setSubmitting(false);
    setAssociationError(undefined);
    setNotice(undefined);
  }, [regarding]);

  // Wired to both the × and the Cancel button (FormModal's `onClose`) — in-flight guard preserved verbatim.
  const handleCancel = React.useCallback(() => {
    if (submitting) return;
    resetState();
    onDismiss();
  }, [submitting, resetState, onDismiss]);

  const handleSubmit = React.useCallback(async () => {
    setAssociationError(undefined);
    setNotice(undefined);

    // ── Validation (NFR-05) — a record association is required for a record-anchored thread ──
    if (!association) {
      setAssociationError('Choose a record to associate this conversation to.');
      return;
    }

    setSubmitting(true);

    // ── Step 1: create the record-anchored thread ──────────────────────────
    let created;
    try {
      created = await createRecordThread(
        {
          name: name.trim() || undefined,
          regardingEntityType: association.entityType,
          regardingRecordId: association.recordId,
          regardingRecordName: association.recordName || undefined,
        },
        client
      );
    } catch (err) {
      // Create failure (fatal): stay open, surface the error, no selection.
      const error = toError(err, 'Failed to create the conversation.');
      onError?.(error);
      if (!mountedRef.current) return;
      setNotice({ text: error.message, intent: 'error' });
      setSubmitting(false);
      return;
    }

    // ── Step 2 (optional): post the first message via the EXISTING send path ──
    const hasBody = body.trim().length > 0;
    if (hasBody) {
      const associations: ICommunicationAssociation[] = [
        {
          entityType: association.entityType,
          entityId: association.recordId,
          entityName: association.recordName || undefined,
        },
      ];
      try {
        await sendCommunication(
          { communicationType: 'message', threadId: created.threadId, body, bodyFormat: 'text', associations },
          client
        );
      } catch (sendErr) {
        // The thread WAS created — select it regardless (non-fatal). Don't conflate a send failure with a
        // create failure; keep the modal open with a non-fatal notice so the user knows to retry in the thread.
        onThreadCreated(created.threadId);
        onError?.(toError(sendErr, 'The first message could not be sent.'));
        if (!mountedRef.current) return;
        setNotice({
          text: "Conversation created, but the first message couldn't be sent — try again in the thread.",
          intent: 'warning',
        });
        setSubmitting(false);
        return;
      }
    }

    // ── Full success ───────────────────────────────────────────────────────
    onThreadCreated(created.threadId);
    if (mountedRef.current) resetState();
    onDismiss();
  }, [association, name, body, client, onThreadCreated, resetState, onDismiss, onError]);

  return (
    <FormModal
      open={open}
      onClose={handleCancel}
      onSubmit={handleSubmit}
      title={title}
      size="md"
      submitLabel={submitting ? 'Creating…' : 'Create'}
      busy={submitting}
      uiScale={uiScale}
    >
      {/* Section 1 — New Thread: name + associate-to-record. The header rule
          + indented body make the fields read as subordinate to the section. */}
      <div className={styles.section} role="group" aria-label="New thread">
        <div className={styles.sectionHeader}>New Thread</div>
        <div className={styles.sectionBody}>
          <Field label="Name">
            <Input
              value={name}
              onChange={(_e, data) => setName(data.value)}
              disabled={submitting}
              placeholder="Conversation name (optional — defaults to the record)"
            />
          </Field>
          {/* AssociateToStep renders its own "Associate To" sub-heading + the
              record-type dropdown + Select Record — no duplicate label here. */}
          <AssociateToStep
            entityTypes={COMMUNICATION_REGARDING_TARGETS}
            navigationService={navigationService}
            value={association}
            onChange={setAssociation}
            disabled={submitting}
            locked={recordLocked}
            variant="compact"
          />
          {associationError && (
            <Text role="alert" className={styles.errorText}>
              {associationError}
            </Text>
          )}
        </div>
      </div>

      {/* Section 2 — Send a message: OPTIONAL plain-text first message. No
          duplicate field label (the section header is enough); fills height. */}
      <div className={styles.messageSection} role="group" aria-label="Send a message">
        <div className={styles.sectionHeader}>Send a message</div>
        <div className={styles.messageBody}>
          <Textarea
            className={styles.messageTextarea}
            value={body}
            onChange={(_e, data) => setBody(data.value)}
            disabled={submitting}
            resize="none"
            aria-label="Message"
            placeholder="Optional — add a first message, or start the conversation empty."
          />
        </div>
      </div>

      {notice && (
        <MessageBar intent={notice.intent} role={notice.intent === 'error' ? 'alert' : 'status'}>
          <MessageBarBody>{notice.text}</MessageBarBody>
        </MessageBar>
      )}
    </FormModal>
  );
};

NewThreadModal.displayName = 'NewThreadModal';

export default NewThreadModal;
