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
 */
import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Button,
  Field,
  Input,
  Textarea,
  Spinner,
  Text,
  MessageBar,
  MessageBarBody,
  makeStyles,
  tokens,
} from '@fluentui/react-components';

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
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    maxWidth: '560px',
    width: '560px',
  },
  content: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  sectionHeading: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  hint: {
    color: tokens.colorNeutralForeground3,
  },
  actionSpinnerRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
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
  return regarding ? { entityType: regarding.entityType, recordId: regarding.id, recordName: regarding.name ?? '' } : null;
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

  const handleDialogOpenChange = React.useCallback(
    (_event: unknown, data: { open: boolean }) => {
      if (!data.open && !submitting) {
        resetState();
        onDismiss();
      }
    },
    [submitting, resetState, onDismiss]
  );

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
        { entityType: association.entityType, entityId: association.recordId, entityName: association.recordName || undefined },
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
    <Dialog open={open} onOpenChange={handleDialogOpenChange} modalType="modal">
      <DialogSurface className={styles.surface} aria-label={title}>
        <DialogBody>
          <DialogTitle>{title}</DialogTitle>
          <DialogContent className={styles.content}>
            {/* Section 1 — New Thread: name + associate-to-record. */}
            <div className={styles.section} role="group" aria-label="New thread">
              <Text className={styles.sectionHeading}>New Thread</Text>
              <Field label="Name">
                <Input
                  value={name}
                  onChange={(_e, data) => setName(data.value)}
                  disabled={submitting}
                  placeholder="Conversation name (optional — defaults to the record)"
                />
              </Field>
              <Field
                label="Associate to record"
                required
                validationState={associationError ? 'error' : 'none'}
                validationMessage={associationError}
              >
                <AssociateToStep
                  entityTypes={COMMUNICATION_REGARDING_TARGETS}
                  navigationService={navigationService}
                  value={association}
                  onChange={setAssociation}
                  disabled={submitting}
                  locked={recordLocked}
                />
              </Field>
            </div>

            {/* Section 2 — Message: OPTIONAL plain-text first message (no rich-text controls). */}
            <div className={styles.section} role="group" aria-label="Message">
              <Text className={styles.sectionHeading}>Message</Text>
              <Field label="Message">
                <Textarea
                  value={body}
                  onChange={(_e, data) => setBody(data.value)}
                  disabled={submitting}
                  resize="vertical"
                  placeholder="Optional — add a first message, or start the conversation empty."
                />
              </Field>
            </div>

            {notice && (
              <MessageBar intent={notice.intent} role={notice.intent === 'error' ? 'alert' : 'status'}>
                <MessageBarBody>{notice.text}</MessageBarBody>
              </MessageBar>
            )}
          </DialogContent>
          <DialogActions>
            {submitting && (
              <div className={styles.actionSpinnerRow} aria-hidden="true">
                <Spinner size="tiny" />
              </div>
            )}
            <Button appearance="secondary" onClick={handleCancel} disabled={submitting}>
              Cancel
            </Button>
            <Button appearance="primary" onClick={handleSubmit} disabled={submitting} aria-busy={submitting}>
              {submitting ? 'Creating…' : 'Create'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

NewThreadModal.displayName = 'NewThreadModal';

export default NewThreadModal;
