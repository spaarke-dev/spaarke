/**
 * RecordMessages — the Messages section of the record detail view
 * (R3 task 051, FR-04 read thread / FR-11 post; spec "message send = thread post, no email, no attachments").
 *
 * WHAT IT DOES. Shows the record's thread oldest-to-newest (sender and time on each message) from
 * `GET /api/v1/external/{root}/{id}/messages`, and lets the caller post a plain-text message with
 * `POST` to the same route (`messages-client.ts` holds the contract). It is shown only for the entities the server
 * exposes a thread for (`MESSAGE_ROOTS`: project, matter, work assignment, invoice, service request); a document has
 * no thread (the server answers 404 for it), so the section is simply absent there.
 *
 * REVIEW CONSTRAINTS (033 / 034):
 *   - 033 K1  The server does not sanitize message bodies. A plain-text body renders as a React text node (escaped by
 *             construction). An HTML body (`sprk_bodyformat` HTML — inbound mail filed on the record) renders ONLY
 *             through the shared hardened `sanitizeEmailHtml` (closed DOMPurify allow-list: no script/iframe/object/form,
 *             no `on*` handlers, http/https/mailto only, anchors forced to noopener). `dangerouslySetInnerHTML` is given
 *             that sanitizer's output and nothing else — never a raw `sprk_body`.
 *   - 034 K3  After a 201 the thread is RE-READ; the returned DTO (null `sendername`, server-time `createdon`) is never
 *             appended.
 *   - 034 K4  The server has no idempotency guard, so Send is disabled while a POST is in flight, and a ref lock closes
 *             the gap before React re-renders (a double-click in one tick).
 *   - 034 K2  Own-versus-other rendering reads the DTO's `isMine`; names and direction are never compared.
 *
 * ERRORS. A refused send (403 no Create / 429 throttle / 400 / 409 / 5xx / network) shows an inline error under the
 * compose box and KEEPS the draft. A 401 on the list or the send is the shared sign-in state and a 403 on the list the
 * shared denied state (`BffAccessState`, as the other pages do). "Sign in again" reloads inside the Teams host and
 * redirects in a browser: `BffAccessState` learns the host from `TeamsHostProvider` (App), so nothing is drilled here.
 *
 * No attachment control and no file input exist here by design (FR-11); the body is capped at 10,000 characters.
 *
 * NEW MESSAGE DIALOG (R4 task 004, FR-03). In the record template the section's New message is the entry point: the host
 * passes `onCreateActionChange`, the inline compose box is not drawn, and the same box (same label, counter, errors, draft
 * handling and send) renders in a `FormModal` that this thread owns, so the dialog is a descendant of the record modal.
 * A send in flight holds the host (`onSendingChange`), a failed send leaves the dialog open with the draft and the same
 * error text, and a sent message closes it and re-reads the thread. Without `onCreateActionChange` (R3's own page) the
 * inline box is unchanged.
 *
 * WHO MAY POST (R4 task 006, FR-06 / NFR-04). In the record template New message is offered only when the thread's read
 * answered `canCreate: true` (task 002: the POST's own decision for this caller); anything else hides it, and a `false`
 * gives the section its read-only line instead. A re-read that fails (other than a sign-in failure, which ends the
 * thread) keeps the messages shown but withdraws New message: the right is no longer known. The inline box of a host without a section action keeps R3's rule (no such host in
 * the app since the template replaced R3's modal).
 *
 * ADR-021: Fluent UI v9 semantic tokens only (no hard-coded colours) — the section inherits the host FluentProvider, so
 * light, dark and the Teams themes work. ADR-012: composes Fluent primitives and the shared sanitizer; the shared
 * EmailComposer / NewThreadModal are internal-plane and Xrm-bound, so only their look is mirrored.
 */
import * as React from 'react';
import {
  makeStyles,
  mergeClasses,
  tokens,
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Spinner,
  Text,
  Textarea,
} from '@fluentui/react-components';
import { SendRegular } from '@fluentui/react-icons';
import { FormModal } from '@spaarke/ui-components/components/SprkModal';
import { sanitizeEmailHtml } from '@spaarke/ui-components/utils/sanitizeEmailHtml';
import {
  BODY_FORMAT_HTML,
  MAX_MESSAGE_BODY_LENGTH,
  listMessages,
  messageProblemOf,
  messageRootFor,
  sendMessage,
  type ExternalMessage,
} from '../../api/messages-client';
import { ApiError } from '../../types';
import { BffAccessState, accessFailureOf, type BffAccessFailure } from '../auth/BffAccessState';
import { TruncatedListNotice } from '../TruncatedListNotice';
import { useChildDialog, useOfferCreateAction, type CreateActionReporter } from '../createAction';
import { createRightOf } from '../record/recordAccess';

// ---------------------------------------------------------------------------
// Copy
// ---------------------------------------------------------------------------

const UNKNOWN_SENDER = 'Unknown sender';
const OWN_SENDER = 'You';
const EMPTY_TEXT = 'No messages yet. Write the first one below.';
const EMPTY_TEXT_DIALOG = 'No messages yet. Use New message above to write the first one.';
/** Template, New message not offered: the section's read-only line (task 006) says why. */
const EMPTY_TEXT_READ_ONLY = 'No messages yet.';
const LOAD_FAILED = 'The existing messages could not be loaded just now.';
const NOT_AVAILABLE = 'Messages are not available for this record.';
const DENIED_TEXT = 'You do not have permission to view messages on this record.';
const SEND_FAILED = 'The message could not be sent just now. Your message has been kept - please try again.';
const SEND_DENIED = 'You do not have permission to post messages on this record. Your message has been kept.';
const SEND_THROTTLED = 'You have sent too many messages. Please try again shortly. Your message has been kept.';
const REFRESH_FAILED = 'Your message was sent, but the thread could not be refreshed just now.';

/** The server's generic 403 detail ("Access denied") says nothing; a specific detail (insufficient rights) is shown. */
const GENERIC_DENIED_DETAIL = 'Access denied';

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

type ThreadState =
  | { status: 'loading' }
  | { status: 'ready'; messages: ExternalMessage[]; truncated: boolean; canCreate: boolean | null }
  | { status: 'error' }
  | { status: 'unavailable' }
  | { status: 'access'; failure: BffAccessFailure };

/** What a failed send shows: the shared sign-in state for a 401, else a sentence. */
export type SendError = { kind: 'signin' } | { kind: 'text'; text: string };

export function sendErrorOf(err: unknown): SendError {
  if (accessFailureOf(err) === 'signin') return { kind: 'signin' };
  if (!(err instanceof ApiError)) return { kind: 'text', text: SEND_FAILED };
  const problem = messageProblemOf(err);
  switch (err.statusCode) {
    case 429:
      return { kind: 'text', text: SEND_THROTTLED };
    case 403: {
      const specific = problem.detail && problem.detail !== GENERIC_DENIED_DETAIL ? problem.detail : null;
      return { kind: 'text', text: specific ? `${specific} Your message has been kept.` : SEND_DENIED };
    }
    // 400 (blank / too long / refused field) and 409 (owner refusal) carry a user-facing server detail.
    case 400:
    case 404:
    case 409:
      return { kind: 'text', text: problem.detail ?? SEND_FAILED };
    default:
      return { kind: 'text', text: SEND_FAILED };
  }
}

/** The local-time rendering of a message's `createdon`; empty when absent or unparseable. */
function formatSentOn(createdon: string | null | undefined): { label: string; iso: string | undefined } {
  if (!createdon) return { label: '', iso: undefined };
  const d = new Date(createdon);
  return Number.isNaN(d.getTime())
    ? { label: '', iso: undefined }
    : { label: d.toLocaleString(), iso: d.toISOString() };
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  heading: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    margin: 0,
  },
  empty: {
    color: tokens.colorNeutralForeground3,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingVerticalM,
  },
  list: {
    listStyleType: 'none',
    margin: 0,
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  item: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  itemMine: {
    backgroundColor: tokens.colorBrandBackground2,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorBrandStroke2}`,
  },
  header: {
    display: 'flex',
    alignItems: 'baseline',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  sender: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  timestamp: {
    color: tokens.colorNeutralForeground3,
  },
  bodyText: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    whiteSpace: 'pre-wrap',
    overflowWrap: 'anywhere',
  },
  // Defence in depth (R3 task 051; ISS-003 / #1636). The shared sanitizer now drops `position:fixed|absolute|sticky`,
  // `inset`, `z-index`, transforms and viewport-sized boxes from an inbound body's `style`, which is what stops a fake
  // sign-in panel painted over the whole app. This containment stays so one sanitizer regression cannot reopen it:
  // `contain: paint` makes this box the containing block for fixed and absolute descendants and clips painting to it;
  // `position: relative` anchors absolute descendants here. overflowX stays `auto` so a wide table scrolls sideways
  // instead of being cut; overflowY is `hidden` because normal flow never overflows an auto-height box, so only an
  // absolutely placed descendant could, and that must be clipped (not given a scrollbar).
  bodyHtml: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    overflowWrap: 'anywhere',
    position: 'relative',
    contain: 'paint',
    overflowX: 'auto',
    overflowY: 'hidden',
    // Parity with the other three consumers: an overflow-clipped flex item may shrink to 0, so a future
    // min-height:0 ancestor would cut the end of a long email with no scrollbar.
    flexShrink: 0,
  },
  compose: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    marginTop: tokens.spacingVerticalS,
  },
  textarea: {
    width: '100%',
  },
  composeFooter: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
  },
  counter: {
    color: tokens.colorNeutralForeground3,
  },
  counterOver: {
    color: tokens.colorPaletteRedForeground1,
  },
});

// ---------------------------------------------------------------------------
// One message
// ---------------------------------------------------------------------------

/**
 * A message body. Plain text is a React text node. HTML goes through `sanitizeEmailHtml` first — the ONLY value ever
 * handed to `dangerouslySetInnerHTML` in this file.
 */
const MessageBody: React.FC<{ message: ExternalMessage }> = ({ message }) => {
  const styles = useStyles();
  const body = message.sprk_body ?? '';
  const isHtml = message.sprk_bodyformat === BODY_FORMAT_HTML;
  const sanitizedHtml = React.useMemo(() => (isHtml && body ? sanitizeEmailHtml(body) : ''), [isHtml, body]);

  if (!body) return null;
  if (isHtml) {
    return (
      <div
        className={styles.bodyHtml}
        data-testid="record-message-html"
        dangerouslySetInnerHTML={{ __html: sanitizedHtml }}
      />
    );
  }
  return (
    <div className={styles.bodyText} data-testid="record-message-text">
      {body}
    </div>
  );
};

const MessageItem: React.FC<{ message: ExternalMessage }> = ({ message }) => {
  const styles = useStyles();
  // Own-vs-other is the server's decision (`isMine`); a name or a direction is never compared (review 034 K2).
  const sender = message.isMine ? OWN_SENDER : message.sendername?.trim() || UNKNOWN_SENDER;
  const sentOn = formatSentOn(message.createdon);
  return (
    <li
      className={mergeClasses(styles.item, message.isMine && styles.itemMine)}
      data-testid="record-message"
      data-mine={message.isMine ? 'true' : 'false'}
    >
      <div className={styles.header}>
        <Text className={styles.sender} data-testid="record-message-sender">
          {sender}
        </Text>
        {sentOn.label && (
          <Text size={200} className={styles.timestamp}>
            <time dateTime={sentOn.iso} data-testid="record-message-time">
              {sentOn.label}
            </time>
          </Text>
        )}
      </div>
      <MessageBody message={message} />
    </li>
  );
};

// ---------------------------------------------------------------------------
// The thread + compose for one supported record
// ---------------------------------------------------------------------------

interface MessageThreadProps {
  root: string;
  recordId: string;
  onSendingChange?: (sending: boolean) => void;
  refreshSignal?: number;
  embedded?: boolean;
  onCreateActionChange?: CreateActionReporter;
}

const MessageThread: React.FC<MessageThreadProps> = ({
  root,
  recordId,
  onSendingChange,
  refreshSignal = 0,
  embedded = false,
  onCreateActionChange,
}) => {
  const styles = useStyles();
  const [thread, setThread] = React.useState<ThreadState>({ status: 'loading' });
  const [refreshFailed, setRefreshFailed] = React.useState(false);
  const [attempt, setAttempt] = React.useState(0);
  const [draft, setDraft] = React.useState('');
  const [sending, setSending] = React.useState(false);
  const [sendError, setSendError] = React.useState<SendError | null>(null);
  // The New message dialog (record template only): its open state and the control that opened it.
  const composeDialog = useChildDialog();

  // Only the latest read may update the thread, and none may once this record is left (stale / out-of-order reads).
  const readSeq = React.useRef(0);
  const dropPendingReads = React.useCallback((): void => {
    readSeq.current++;
  }, []);
  // Closes the gap between a click and React re-rendering the disabled button: a second click in the same tick.
  const sendLock = React.useRef(false);

  // Tells the host a send is in flight (so it can hold off navigating away and losing the result). The latest callback
  // is read through a ref so a new function identity never re-fires the effect, and unmount always reports "not sending".
  const onSendingChangeRef = React.useRef(onSendingChange);
  onSendingChangeRef.current = onSendingChange;
  React.useEffect(() => {
    onSendingChangeRef.current?.(sending);
  }, [sending]);
  React.useEffect(
    () => () => {
      onSendingChangeRef.current?.(false);
    },
    []
  );

  const read = React.useCallback(
    async (mode: 'initial' | 'refresh'): Promise<void> => {
      const seq = ++readSeq.current;
      try {
        const result = await listMessages(root, recordId);
        if (seq !== readSeq.current) return;
        setThread({
          status: 'ready',
          messages: result.value ?? [],
          truncated: result.truncated === true,
          // Task 006: the POST's own decision for this caller (task 002); a missing one is "not said", never "can".
          canCreate: typeof result.canCreate === 'boolean' ? result.canCreate : null,
        });
        setRefreshFailed(false);
      } catch (err: unknown) {
        if (seq !== readSeq.current) return;
        const failure = accessFailureOf(err);
        if (mode === 'refresh') {
          // A 401 is the same sign-in state the list uses (the session ended; nothing more can be read or sent).
          if (failure === 'signin') {
            setThread({ status: 'access', failure });
            return;
          }
          // Only the re-read failed. The messages on screen stay, and the notice says so; but the posting right they
          // came with is no longer known (task 006 fix round 1: a 403 here may mean it was withdrawn), so it is
          // forgotten and New message withdrawn until a read answers again. A refresh that overtook the initial read
          // must not leave the spinner up for ever: that read is dropped.
          setRefreshFailed(true);
          setThread(t =>
            t.status === 'loading' ? { status: 'error' } : t.status === 'ready' ? { ...t, canCreate: null } : t
          );
          return;
        }
        if (failure) setThread({ status: 'access', failure });
        else if (err instanceof ApiError && err.statusCode === 404) setThread({ status: 'unavailable' });
        else setThread({ status: 'error' });
      }
    },
    [root, recordId]
  );

  React.useEffect(() => {
    setThread({ status: 'loading' });
    void read('initial');
    return dropPendingReads;
    // `attempt` re-runs the initial read for "Try again".
  }, [read, attempt, dropPendingReads]);

  // A host that posted to this thread itself (the service request recall) bumps `refreshSignal`: re-read, never append.
  // The first value is the initial read's own, so only a later change refreshes.
  const lastRefreshSignal = React.useRef(refreshSignal);
  React.useEffect(() => {
    if (lastRefreshSignal.current === refreshSignal) return;
    lastRefreshSignal.current = refreshSignal;
    void read('refresh');
  }, [refreshSignal, read]);

  const text = draft.trim();
  const tooLong = text.length > MAX_MESSAGE_BODY_LENGTH;
  const canSend = text.length > 0 && !tooLong && !sending;

  const handleSend = async (): Promise<void> => {
    if (sendLock.current || !canSend) return;
    sendLock.current = true;
    setSending(true);
    setSendError(null);
    try {
      await sendMessage(root, recordId, text);
    } catch (err: unknown) {
      // The draft stays exactly as typed.
      setSendError(sendErrorOf(err));
      sendLock.current = false;
      setSending(false);
      return;
    }
    // Sent: the box is emptied and the thread re-read from the server (never append the 201 body — review 034 K3).
    setDraft('');
    sendLock.current = false;
    setSending(false);
    composeDialog.hide();
    await read('refresh');
  };

  const showCompose = thread.status !== 'access' && thread.status !== 'unavailable';
  const overBy = text.length - MAX_MESSAGE_BODY_LENGTH;

  // In the record template the section's New message replaces the inline box, and the dialog stays here; the draft is kept
  // when it is cancelled. Task 006 (FR-06): it is offered only when the LATEST thread read answered `canCreate: true` (the
  // POST's own decision, task 002). Not loaded yet, failed, refused, unavailable or silent = not offered (fail closed);
  // a failed re-read keeps the messages on screen but forgets the right, so New message is withdrawn until a read answers
  // again. The right goes with the offer, for "Your access" and the read-only line.
  const composeInDialog = onCreateActionChange !== undefined;
  const right = createRightOf({
    loaded: thread.status === 'ready',
    listCanCreate: thread.status === 'ready' ? thread.canCreate : null,
  });
  const offered = right === true;
  useOfferCreateAction(onCreateActionChange, offered, composeDialog.show, right);
  const closeCompose = (): void => {
    if (sendLock.current) return;
    composeDialog.hide();
  };
  // The thread read can turn the offer off while the dialog is open (a read that answers 403 or "unavailable"): the dialog
  // then unmounts with the offer, so close it through hide() too, which resets the open state and returns focus (to the
  // record dialog, the header button having gone) instead of leaving it on the page body.
  const { open: composeOpen, hide: hideCompose } = composeDialog;
  React.useEffect(() => {
    if (composeInDialog && !offered && composeOpen) hideCompose();
  }, [composeInDialog, offered, composeOpen, hideCompose]);

  const counter = (
    <Text
      size={200}
      className={mergeClasses(styles.counter, tooLong && styles.counterOver)}
      role={tooLong ? 'alert' : undefined}
      data-testid="record-messages-counter"
    >
      {tooLong
        ? `${overBy.toLocaleString()} over the ${MAX_MESSAGE_BODY_LENGTH.toLocaleString()} character limit`
        : `${text.length.toLocaleString()} / ${MAX_MESSAGE_BODY_LENGTH.toLocaleString()}`}
    </Text>
  );
  const textarea = (
    <Textarea
      className={styles.textarea}
      aria-label="Write a message"
      placeholder="Write a message..."
      resize="vertical"
      rows={3}
      value={draft}
      disabled={sending}
      onChange={(_e, data) => setDraft(data.value)}
    />
  );
  const sendErrors = (
    <>
      {sendError?.kind === 'signin' && <BffAccessState failure="signin" />}
      {sendError?.kind === 'text' && (
        <MessageBar intent="error" role="alert" data-testid="record-messages-send-error">
          <MessageBarBody>{sendError.text}</MessageBarBody>
        </MessageBar>
      )}
    </>
  );

  // Embedded in a host's section card (the record template), the card is the region and draws the heading.
  const Root = embedded ? 'div' : 'section';
  return (
    <Root
      className={styles.section}
      aria-label={embedded ? undefined : 'Messages'}
      data-testid="record-detail-messages"
    >
      {!embedded && <h3 className={styles.heading}>Messages</h3>}

      {thread.status === 'loading' && <Spinner size="small" label="Loading messages..." />}

      {thread.status === 'access' && <BffAccessState failure={thread.failure} deniedMessage={DENIED_TEXT} />}

      {thread.status === 'unavailable' && (
        <div className={styles.empty} role="status" data-testid="record-messages-unavailable">
          {NOT_AVAILABLE}
        </div>
      )}

      {/* Shown whatever the thread status: the compose box is available while loading and after a failed read, so a
          successful send can be followed by a failed re-read there too. */}
      {refreshFailed && thread.status !== 'access' && (
        <MessageBar intent="warning" data-testid="record-messages-refresh-failed">
          <MessageBarBody>{REFRESH_FAILED}</MessageBarBody>
          {thread.status === 'ready' && (
            <MessageBarActions>
              <Button appearance="secondary" size="small" onClick={() => void read('refresh')}>
                Refresh
              </Button>
            </MessageBarActions>
          )}
        </MessageBar>
      )}

      {thread.status === 'error' && (
        <MessageBar intent="error" data-testid="record-messages-error">
          <MessageBarBody>{LOAD_FAILED}</MessageBarBody>
          <MessageBarActions>
            <Button appearance="secondary" size="small" onClick={() => setAttempt(n => n + 1)}>
              Try again
            </Button>
          </MessageBarActions>
        </MessageBar>
      )}

      {thread.status === 'ready' && (
        <>
          <TruncatedListNotice truncated={thread.truncated} shown={thread.messages.length} noun="messages" />
          {thread.messages.length === 0 && !thread.truncated ? (
            <div className={styles.empty} role="status" data-testid="record-messages-empty">
              {composeInDialog ? (offered ? EMPTY_TEXT_DIALOG : EMPTY_TEXT_READ_ONLY) : EMPTY_TEXT}
            </div>
          ) : (
            <ul className={styles.list} aria-label="Message thread" data-testid="record-messages-list">
              {thread.messages.map((m, i) => (
                <MessageItem key={m.sprk_communicationid || i} message={m} />
              ))}
            </ul>
          )}
        </>
      )}

      {showCompose && !composeInDialog && (
        <div className={styles.compose} data-testid="record-messages-compose">
          {textarea}
          <div className={styles.composeFooter}>
            {counter}
            <Button
              appearance="primary"
              icon={sending ? <Spinner size="tiny" /> : <SendRegular />}
              disabled={!canSend}
              onClick={() => void handleSend()}
            >
              {sending ? 'Sending...' : 'Send'}
            </Button>
          </div>

          {sendErrors}
        </div>
      )}

      {/* The record template's New message (task 004): a FormModal, Cancel left, Send right. Esc does nothing (modal
          standard: an explicit dialog keeps a draft). */}
      {composeInDialog && offered && (
        <FormModal
          open={composeDialog.open}
          onClose={closeCompose}
          onSubmit={() => void handleSend()}
          title="New message"
          submitLabel={sending ? 'Sending...' : 'Send'}
          submitDisabled={!canSend}
          busy={sending}
        >
          <div className={styles.compose} data-testid="record-messages-compose">
            {textarea}
            {counter}
            {sendErrors}
          </div>
        </FormModal>
      )}
    </Root>
  );
};

// ---------------------------------------------------------------------------
// Public component
// ---------------------------------------------------------------------------

export interface RecordMessagesProps {
  /** Logical name of the record's entity. Entities with no message thread (documents) render nothing. */
  entity: string;
  /** The record's id (a GUID the caller has already validated). */
  recordId: string;
  /** Called with `true` while a send is in flight and `false` otherwise (and on unmount), so a host can hold navigation. */
  onSendingChange?: (sending: boolean) => void;
  /** Changing this number re-reads the thread (a host posted to it elsewhere, e.g. the service request recall). */
  refreshSignal?: number;
  /**
   * Rendered inside a host's own titled section (the record template's section card): no region and no heading of its
   * own, so the page has one "Messages" region and one heading. Default false (its own section and heading).
   */
  embedded?: boolean;
  /**
   * Record template only (task 004, FR-03): the section's New message is the entry point. The thread then draws no inline
   * compose box, offers the action through this callback (task 006: only once the thread's read answered
   * `canCreate: true`) and renders the compose box in a `FormModal` of its own. Omit it for R3's own page.
   */
  onCreateActionChange?: CreateActionReporter;
}

export const RecordMessages: React.FC<RecordMessagesProps> = ({
  entity,
  recordId,
  onSendingChange,
  refreshSignal,
  embedded,
  onCreateActionChange,
}) => {
  const root = messageRootFor(entity);
  if (!root) return null;
  return (
    <MessageThread
      root={root}
      recordId={recordId}
      onSendingChange={onSendingChange}
      refreshSignal={refreshSignal}
      embedded={embedded}
      onCreateActionChange={onCreateActionChange}
    />
  );
};

export default RecordMessages;
