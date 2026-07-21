/**
 * NewThreadModal.tsx — create (find-or-create) a Direct conversation thread (task 024, FR-11).
 *
 * A Fluent v9 modal reachable from the `<ConversationWorkspace />` shell's ＋
 * affordance (task 012's `onCreateThread`). It composes the EXISTING recipient +
 * body composer parts (`RecipientField`, `BodyEditor`) and the EXISTING regarding
 * mechanism (`AssociationChips`, ADR-024) — nothing here is re-implemented (NFR-06).
 *
 * On submit it FINDS-OR-CREATES the thread via `POST /api/communications/threads/direct`
 * (`startDirectThread`) and hands the returned `threadId` to the host so the shell
 * can select it (`onThreadCreated`). Because the endpoint is find-or-create + keyed
 * on the participant pair, re-submitting the same participant reuses the SAME thread —
 * there is NO client-side de-duplication or second create path (task 024 escalation
 * trigger honored).
 *
 * ── CONTRACT ADAPTATION (documented deviation) ──
 * `POST /threads/direct` (`StartDirectThreadRequest`) is deliberately NARROW: it
 * carries ONLY `otherParticipantSystemUserId` (exactly ONE resolved Spaarke
 * `systemuser`). It has NO server field for a thread name, a description, a
 * thread-level regarding association, a recipient LIST, or a first-message body.
 * Rather than invent server fields (parent directive: "do NOT invent server
 * fields"), the modal maps to the real contract:
 *   • Participant (reused `RecipientField`) → `otherParticipantSystemUserId`
 *     (the recipient RESOLVED to a `systemuser` from the directory — a free-text
 *     email / `contact` cannot start an internal 1:1 Direct thread). Direct
 *     threads are 1:1, so EXACTLY ONE resolved systemuser is allowed (M3).
 *   • Thread-level regarding linkage RIDES THE FIRST MESSAGE: the endpoint has no
 *     thread-level regarding field, so an optional `regarding` (reused
 *     `AssociationChips`, ADR-024) is persisted only via the first message's
 *     `associations`, posted through the EXISTING send engine
 *     (`sendCommunication({ communicationType: 'message', threadId, associations })`,
 *     ADR-045). Because that link exists ONLY on a message, supplying a `regarding`
 *     REQUIRES a first-message body — submit is blocked otherwise so the promised
 *     link can never be silently dropped (M2).
 *   • A create-success whose FIRST-MESSAGE send fails is NON-FATAL: the thread was
 *     created, so it is still selected (`onThreadCreated`) and a non-fatal notice is
 *     shown — a send failure is never conflated with a create failure (M1).
 *   • Thread `name` / `description` are OMITTED: the endpoint persists neither and a
 *     rename-after-find pivot is unsafe (the response has no create-vs-find
 *     discriminator, so it would clobber an existing thread's name). A future
 *     backend amendment (Path B) is the upgrade path; out of scope here.
 *
 * Context-agnostic (ADR-012): no `Xrm`/`ComponentFramework`. All platform I/O is
 * injected (`authenticatedFetch`, `onSearchRecipients`). No `@spaarke/auth` import
 * (ADR-028). Fluent v9 semantic tokens only; dark mode passes through the host
 * `FluentProvider` (ADR-021).
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
  Spinner,
  Text,
  MessageBar,
  MessageBarBody,
  makeStyles,
  tokens,
} from '@fluentui/react-components';

import { RecipientField, BodyEditor, AssociationChips } from '../EmailComposer';
import type { IRecipient, EmailComposerBodyFormat } from '../EmailComposer';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import type { ILookupItem } from '../../types/LookupTypes';
import {
  sendCommunication,
  type ICommunicationAssociation,
} from '../../services/communicationApi';
import {
  startDirectThread,
  type IThreadListApiClientOptions,
} from '../../services/communicationThreadListApi';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Optional record association (ADR-024) for the new thread's first message. The
 * host supplies it from record context (e.g. the workspace's regarding scope);
 * omit it for a record-less Direct thread. Mirrors `ISendEmailDialogRegarding`
 * so callers pass the SAME shape they already pass to the send dialog.
 */
export interface INewThreadModalRegarding {
  /** Dataverse logical name — one of the ADR-024 regarding family (e.g. `sprk_matter`). */
  entityType: string;
  /** Regarding record GUID. */
  id: string;
  /** Optional display name for the association chip. */
  name?: string;
}

export interface INewThreadModalProps {
  /** Controls Dialog visibility. */
  open: boolean;
  /** Fired on cancel, Esc/backdrop dismiss, and after a successful create. */
  onDismiss: () => void;
  /** Injected auth (ADR-028) — forwarded to `startDirectThread` + `sendCommunication`. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** Host only, no `/api` — forwarded exactly as the communication API wrappers expect. */
  bffBaseUrl?: string;
  /** Directory search for the participant picker — host binds `searchUsersAndContacts`. */
  onSearchRecipients?: (query: string) => Promise<ILookupItem[]>;
  /**
   * Success callback — the find-or-create thread id. The shell wires this to its
   * thread-selection path (task 012) so the new/reused thread becomes selected.
   * Also fired when the thread was created but its first message failed to send
   * (M1, non-fatal) — the thread exists and must be selectable.
   */
  onThreadCreated: (threadId: string) => void;
  /**
   * Optional record association for the new thread (ADR-024). Present → record-
   * associated: the link rides the first message's `associations`, so a body is
   * REQUIRED (M2). Absent → record-less Direct thread. Uses the EXISTING
   * association mechanism — no second regarding path.
   */
  regarding?: INewThreadModalRegarding;
  /** Fired on any submit failure — create failure (fatal) or first-message send failure (non-fatal). */
  onError?: (error: Error) => void;
  /** Optional Dialog title override. Default "New conversation". */
  title?: string;
}

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
    gap: tokens.spacingVerticalM,
  },
  regardingBlock: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  regardingLabel: {
    color: tokens.colorNeutralForeground3,
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

/**
 * Recipients RESOLVED to a Dataverse `systemuser` (carry `sourceId` =
 * `systemuserid` + `entityType === 'systemuser'`, per `RecipientField` / FR-10).
 * A Direct thread is 1:1, so exactly ONE of these may be present at submit —
 * free-text / `contact` chips are ignored (they cannot start an internal 1:1).
 */
function resolvedSystemUsers(recipients: IRecipient[]): IRecipient[] {
  return recipients.filter(r => r.entityType === 'systemuser' && !!r.sourceId);
}

function mapBodyFormatToSend(format: EmailComposerBodyFormat): 'html' | 'text' {
  return format === 'HTML' ? 'html' : 'text';
}

function toError(err: unknown, fallback: string): Error {
  return err instanceof Error ? err : new Error(fallback);
}

/** Inline notice surfaced to the user (fatal error vs. non-fatal warning). */
interface INotice {
  text: string;
  intent: 'error' | 'warning';
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Create-thread modal (FR-11). See file header for the /threads/direct contract
 * adaptation and reuse rationale.
 */
export const NewThreadModal: React.FC<INewThreadModalProps> = ({
  open,
  onDismiss,
  authenticatedFetch,
  bffBaseUrl,
  onSearchRecipients,
  onThreadCreated,
  regarding,
  onError,
  title = 'New conversation',
}) => {
  const styles = useStyles();

  const [participants, setParticipants] = React.useState<IRecipient[]>([]);
  const [body, setBody] = React.useState('');
  const [bodyFormat, setBodyFormat] = React.useState<EmailComposerBodyFormat>('PlainText');
  const [submitting, setSubmitting] = React.useState(false);
  const [participantError, setParticipantError] = React.useState<string | undefined>(undefined);
  const [bodyError, setBodyError] = React.useState<string | undefined>(undefined);
  const [notice, setNotice] = React.useState<INotice | undefined>(undefined);

  // N1: guard post-await setState — the host owns `open` and may unmount this
  // modal mid-flight (React 16.14 PCF target). `onThreadCreated` / `onDismiss`
  // are host callbacks and stay safe; only the LOCAL setState calls are gated.
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

  // Fold the optional regarding into the EXISTING association mechanism (ADR-024)
  // — a single `ICommunicationAssociation` rendered read-only via `AssociationChips`
  // and carried onto the first message's send payload. No second regarding path.
  const associations = React.useMemo<ICommunicationAssociation[]>(
    () =>
      regarding
        ? [{ entityType: regarding.entityType, entityId: regarding.id, entityName: regarding.name }]
        : [],
    [regarding]
  );

  const regardingLabel = regarding ? regarding.name ?? regarding.entityType : '';

  const resetState = React.useCallback(() => {
    setParticipants([]);
    setBody('');
    setBodyFormat('PlainText');
    setSubmitting(false);
    setParticipantError(undefined);
    setBodyError(undefined);
    setNotice(undefined);
  }, []);

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
    setParticipantError(undefined);
    setBodyError(undefined);
    setNotice(undefined);

    // ── Validation (NFR-05) ────────────────────────────────────────────────
    if (participants.length === 0) {
      setParticipantError('Add a participant to start a conversation.');
      return;
    }
    const resolved = resolvedSystemUsers(participants);
    if (resolved.length === 0) {
      setParticipantError('Select a Spaarke user from the directory to start a direct conversation.');
      return;
    }
    // M3: Direct threads are 1:1 — more than one resolved Spaarke user cannot be
    // silently reduced to the first.
    if (resolved.length > 1) {
      setParticipantError('Direct conversations are 1:1 — remove the extra participants.');
      return;
    }
    const participant = resolved[0];

    const hasBody = body.trim().length > 0;
    // M2: a thread-level regarding is persisted ONLY via the first message, so a
    // body is required when a regarding is supplied — never drop the link silently.
    if (regarding && !hasBody) {
      setBodyError(`Add a first message to link this conversation to ${regardingLabel}.`);
      return;
    }

    setSubmitting(true);

    // ── Step 1: find-or-create the thread ──────────────────────────────────
    let created;
    try {
      created = await startDirectThread(
        { otherParticipantSystemUserId: participant.sourceId as string },
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
    if (hasBody) {
      try {
        await sendCommunication(
          {
            communicationType: 'message',
            threadId: created.threadId,
            body,
            bodyFormat: mapBodyFormatToSend(bodyFormat),
            associations: associations.length > 0 ? associations : undefined,
          },
          client
        );
      } catch (sendErr) {
        // M1: the thread WAS created — select it regardless (non-fatal). Do NOT
        // conflate a send failure with a create failure; keep the modal open with
        // a non-fatal notice so the user knows to retry inside the thread.
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
  }, [
    participants,
    body,
    bodyFormat,
    regarding,
    regardingLabel,
    associations,
    client,
    onThreadCreated,
    resetState,
    onDismiss,
    onError,
  ]);

  return (
    <Dialog open={open} onOpenChange={handleDialogOpenChange} modalType="modal">
      <DialogSurface className={styles.surface} aria-label={title}>
        <DialogBody>
          <DialogTitle>{title}</DialogTitle>
          <DialogContent className={styles.content}>
            {regarding && (
              <div className={styles.regardingBlock} role="group" aria-label="Linked record">
                <Text size={200} className={styles.regardingLabel}>
                  Linked record
                </Text>
                <AssociationChips associations={associations} />
              </div>
            )}

            <RecipientField
              label="Participant"
              required
              value={participants}
              onChange={setParticipants}
              onSearch={onSearchRecipients}
              disabled={submitting}
              errorMessage={participantError}
              placeholder="Search for a Spaarke user…"
            />

            <BodyEditor
              value={body}
              format={bodyFormat}
              onChange={setBody}
              onFormatChange={setBodyFormat}
              readOnly={submitting}
              errorMessage={bodyError}
            />
            <Text size={200} className={styles.hint}>
              {regarding
                ? 'Add a first message to link this conversation to the record.'
                : 'Optional — add a first message, or start the conversation empty.'}
            </Text>

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
            {/* N2: no static aria-label — the visible "Creating…" text is the
                accessible name, and aria-busy announces the in-flight state. */}
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
