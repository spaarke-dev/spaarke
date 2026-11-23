/**
 * NewDirectMessageModal.tsx — start (or reopen) a 1:1 Direct conversation with one internal user (fm task 055).
 *
 * Reached from the `<ConversationWorkspace />` Messages tab's ＋. One person (the Dataverse lookup side pane on
 * `systemuser`, limited by `INTERNAL_USER_LOOKUP_FILTERS` to enabled people other than the signed-in user) and an
 * optional first message.
 *
 * ── CONTRACT ──
 * Submit calls `POST /api/communications/threads/direct` (`startDirectThread`). That is find-or-create: a second "new"
 * message to the same person opens the same conversation. The server applies the Direct participant rule (an active
 * internal user) and answers one neutral 400 otherwise; that text is shown inline. When a first message is given it is
 * posted on the conversation through the existing send path (`sendCommunication`, the same request the message pane
 * sends); a send failure is non-fatal: the conversation exists, so it is selected and a warning stays on screen.
 *
 * ── PRIVATE MODE (fm task 056) ──
 * With `privateContext` the modal is the record's "Send privately…" dialog: the same person picker, with the record fixed
 * as context and a first message REQUIRED. Submit is `startDirectThread` then `sendCommunication` with `privateContext`. The
 * server may refuse the send (403 recipient / sender access, 503 check unavailable, 409 columns missing, 400): the
 * owner's wording is shown (`privateSendRefusalMessage`), the dialog stays open, and the pair's thread id is kept for the
 * chosen person so a retry never starts a second conversation. A recipient refusal also offers "Send as a direct message
 * instead", which re-sends the same text to the same pair thread WITHOUT the record context.
 *
 * Differs from `NewThreadModal` (record-anchored: a name and a required record, `createRecordThread`); the two share only
 * the `FormModal` shell and the message box (fm task 055 notes, §11).
 *
 * Context-agnostic (ADR-012): no `Xrm`/`ComponentFramework`; `authenticatedFetch` and `navigationService` are injected.
 * Fluent v9 tokens only (ADR-021).
 */
import * as React from 'react';
import {
  Button,
  Textarea,
  Text,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  makeStyles,
  tokens,
} from '@fluentui/react-components';

import { FormModal } from '../SprkModal/presets/FormModal';
import { DataverseLookupField } from '../LookupField/DataverseLookupField';
import { INTERNAL_USER_LOOKUP_FILTERS } from '../LookupField/lookupFilters';
import type { ILookupItem } from '../../types/LookupTypes';
import type { INavigationService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import {
  isPrivateRecipientRefusal,
  privateSendRefusalMessage,
  sendCommunication,
} from '../../services/communicationApi';
import { startDirectThread, type IThreadListApiClientOptions } from '../../services/communicationThreadListApi';

export interface INewDirectMessageModalProps {
  /** Controls visibility. */
  open: boolean;
  /** Fired on cancel and after a successful start. */
  onDismiss: () => void;
  /** Injected auth (ADR-028), forwarded to `startDirectThread` and `sendCommunication`. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** Host only, no `/api`. */
  bffBaseUrl?: string;
  /** Opens the `systemuser` lookup side pane. The host binds `createXrmNavigationService()`. */
  navigationService: INavigationService;
  /**
   * The conversation's thread id and the person's display name. The shell selects the thread. Also fired when the
   * conversation was started but its first message failed to send (non-fatal). `sentPrivately` is true only when a
   * private message about the record went out (private mode); it is false for the plain Direct flow and for the
   * "send as a direct message instead" fallback.
   */
  onConversationStarted: (threadId: string, counterpartName: string, sentPrivately?: boolean) => void;
  /**
   * Private mode (fm task 056): the record the message is about. `name` is shown when the host knows it; otherwise the
   * dialog says "this {record kind}". Valid only for the three core record types the server accepts.
   */
  privateContext?: { entityType: string; id: string; name?: string };
  /** Fired on any failure: start (fatal) or first-message send (non-fatal). */
  onError?: (error: Error) => void;
  /** Title override. Default "New message". */
  title?: string;
  /** The `--sprk-ui-scale` factor, forwarded to `FormModal`. */
  uiScale?: number;
}

const useStyles = makeStyles({
  errorText: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
  },
  messageBody: {
    display: 'flex',
    flex: '1 1 auto',
    minHeight: '120px',
  },
  messageTextarea: {
    width: '100%',
    height: '100%',
    '& textarea': {
      height: '100%',
      maxHeight: 'none',
    },
  },
});

interface INotice {
  text: string;
  intent: 'error' | 'warning';
  /** Private mode, recipient refusal only: offer "Send as a direct message instead". */
  offerDirect?: boolean;
}

const RECORD_KIND_LABEL: Record<string, string> = {
  sprk_matter: 'matter',
  sprk_project: 'project',
  sprk_workassignment: 'work assignment',
};

function toError(err: unknown, fallback: string): Error {
  return err instanceof Error ? err : new Error(fallback);
}

export const NewDirectMessageModal: React.FC<INewDirectMessageModalProps> = ({
  open,
  onDismiss,
  authenticatedFetch,
  bffBaseUrl,
  navigationService,
  onConversationStarted,
  onError,
  privateContext,
  title,
  uiScale,
}) => {
  const styles = useStyles();

  const [person, setPerson] = React.useState<ILookupItem | null>(null);
  const [body, setBody] = React.useState('');
  const [submitting, setSubmitting] = React.useState(false);
  const [personError, setPersonError] = React.useState<string | undefined>(undefined);
  const [notice, setNotice] = React.useState<INotice | undefined>(undefined);
  // A submit disables its button while it runs, which drops focus out of the dialog; Fluent's focus manager (tabster)
  // then marks the still-open dialog aria-hidden, so the notice below is not exposed to assistive technology. Return
  // focus to the message box when a notice appears (also where the user retypes after a refusal).
  const bodyRef = React.useRef<HTMLTextAreaElement>(null);
  React.useEffect(() => {
    if (notice) bodyRef.current?.focus();
  }, [notice]);

  const [bodyError, setBodyError] = React.useState<string | undefined>(undefined);
  // Private mode: the pair's thread for the chosen person, kept after the first start so a retry (after a refusal) does
  // not call find-or-create again.
  const pairRef = React.useRef<{ personId: string; threadId: string } | undefined>(undefined);
  const isPrivate = !!privateContext;
  const effectiveTitle = title ?? (isPrivate ? 'Send privately' : 'New message');

  // The host owns `open` and may unmount this modal while a request is in flight; only local setState is gated.
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

  const resetState = React.useCallback(() => {
    setPerson(null);
    setBody('');
    setSubmitting(false);
    setPersonError(undefined);
    setBodyError(undefined);
    setNotice(undefined);
    pairRef.current = undefined;
  }, []);

  const handleCancel = React.useCallback(() => {
    if (submitting) return;
    resetState();
    onDismiss();
  }, [submitting, resetState, onDismiss]);

  const handleSubmit = React.useCallback(async () => {
    setPersonError(undefined);
    setBodyError(undefined);
    setNotice(undefined);

    if (!person) {
      setPersonError('Choose the person to message.');
      return;
    }
    if (privateContext && !body.trim()) {
      setBodyError('Write the message to send.');
      return;
    }

    setSubmitting(true);

    let threadId: string;
    try {
      const kept = pairRef.current;
      threadId =
        privateContext && kept && kept.personId === person.id
          ? kept.threadId
          : (await startDirectThread({ otherParticipantSystemUserId: person.id }, client)).threadId;
      if (privateContext) pairRef.current = { personId: person.id, threadId };
    } catch (err) {
      const error = toError(err, 'The conversation could not be started.');
      onError?.(error);
      if (!mountedRef.current) return;
      setNotice({ text: error.message, intent: 'error' });
      setSubmitting(false);
      return;
    }

    const text = body.trim();
    if (privateContext) {
      try {
        await sendCommunication(
          {
            communicationType: 'message',
            threadId,
            body: text,
            bodyFormat: 'text',
            privateContext: { entityType: privateContext.entityType, id: privateContext.id },
          },
          client
        );
      } catch (sendErr) {
        // A refusal keeps the dialog open with the owner's wording. The pair thread (new or reused) is NOT handed to the
        // host: nothing was sent, and the record's thread list must not gain an entry for it.
        onError?.(toError(sendErr, 'The private message could not be sent.'));
        if (!mountedRef.current) return;
        setNotice({
          text: privateSendRefusalMessage(sendErr),
          intent: 'error',
          offerDirect: isPrivateRecipientRefusal(sendErr),
        });
        setSubmitting(false);
        return;
      }
      onConversationStarted(threadId, person.name, true);
      if (mountedRef.current) resetState();
      onDismiss();
      return;
    }
    if (text) {
      try {
        await sendCommunication({ communicationType: 'message', threadId, body: text, bodyFormat: 'text' }, client);
      } catch (sendErr) {
        onConversationStarted(threadId, person.name);
        onError?.(toError(sendErr, 'The first message could not be sent.'));
        if (!mountedRef.current) return;
        setNotice({
          text: "The conversation is open, but the message couldn't be sent — try again in the conversation.",
          intent: 'warning',
        });
        setSubmitting(false);
        return;
      }
    }

    onConversationStarted(threadId, person.name);
    if (mountedRef.current) resetState();
    onDismiss();
  }, [person, body, client, privateContext, onConversationStarted, resetState, onDismiss, onError]);

  // Recipient refusal only: send the same text to the same pair thread as an ordinary Direct message (no record context).
  const handleSendDirectInstead = React.useCallback(async () => {
    const kept = pairRef.current;
    const text = body.trim();
    if (!person || !kept || kept.personId !== person.id || !text) return;
    setNotice(undefined);
    setSubmitting(true);
    try {
      await sendCommunication(
        { communicationType: 'message', threadId: kept.threadId, body: text, bodyFormat: 'text' },
        client
      );
    } catch (err) {
      onError?.(toError(err, 'The message could not be sent.'));
      if (!mountedRef.current) return;
      setNotice({ text: toError(err, 'The message could not be sent.').message, intent: 'error' });
      setSubmitting(false);
      return;
    }
    onConversationStarted(kept.threadId, person.name, false);
    if (mountedRef.current) resetState();
    onDismiss();
  }, [person, body, client, onConversationStarted, resetState, onDismiss, onError]);

  return (
    <FormModal
      open={open}
      onClose={handleCancel}
      onSubmit={handleSubmit}
      title={effectiveTitle}
      size="md"
      submitLabel={isPrivate ? (submitting ? 'Sending…' : 'Send') : submitting ? 'Starting…' : 'Start'}
      busy={submitting}
      uiScale={uiScale}
    >
      {privateContext && (
        <Text>
          {`About ${privateContext.name ?? `this ${RECORD_KIND_LABEL[privateContext.entityType] ?? 'record'}`}. Only you and the person you choose can read this message.`}
        </Text>
      )}
      <DataverseLookupField
        label="To"
        required
        entityType="systemuser"
        value={person}
        onChange={item => {
          setPerson(item);
          setPersonError(undefined);
          // Picking replaces the focused "Select To" button with the chosen person, which drops focus out of the dialog;
          // Fluent's focus manager (tabster) then marks the open dialog aria-hidden. Move focus on to the message box,
          // the next field.
          if (item) requestAnimationFrame(() => bodyRef.current?.focus());
        }}
        navigationService={navigationService}
        filters={INTERNAL_USER_LOOKUP_FILTERS}
      />
      {personError && (
        <Text role="alert" className={styles.errorText}>
          {personError}
        </Text>
      )}

      <div className={styles.messageBody}>
        <Textarea
          className={styles.messageTextarea}
          value={body}
          onChange={(_e, data) => setBody(data.value)}
          ref={bodyRef}
          disabled={submitting}
          resize="none"
          aria-label="Message"
          placeholder={
            isPrivate
              ? 'Write your private message.'
              : 'Optional — add a first message, or open the conversation empty.'
          }
        />
      </div>
      {bodyError && (
        <Text role="alert" className={styles.errorText}>
          {bodyError}
        </Text>
      )}

      {notice && (
        <MessageBar intent={notice.intent} role={notice.intent === 'error' ? 'alert' : 'status'}>
          <MessageBarBody>{notice.text}</MessageBarBody>
          {notice.offerDirect && (
            <MessageBarActions>
              <Button size="small" onClick={() => void handleSendDirectInstead()} disabled={submitting}>
                Send as a direct message instead
              </Button>
            </MessageBarActions>
          )}
        </MessageBar>
      )}
    </FormModal>
  );
};

NewDirectMessageModal.displayName = 'NewDirectMessageModal';

export default NewDirectMessageModal;
