/**
 * ServiceRequestActions — "Delete request" and "Send recall request" on the service request detail view
 * (spaarke-SPA-external-access-platform-r3 task 054, FR-26; owner decisions 2026-10-11).
 *
 * WHO SEES IT. Only the request's requester: the record's `sprk_requestedby` is the signed-in caller's contact
 * (`isRequestedByCaller`, from the `/me` identity the detail view already read). That is a convenience: the server's
 * requester gate decides every DELETE, and a wrong guess comes back as the uniform 403 (the shared denial message).
 *
 * THE FLOW. `DELETE /api/v1/external/servicerequests/{id}` through the shared confirm-then-delete flow (`useDeleteOwn`,
 * `DeleteOwnConfirm`, ADR-050 `ConfirmModal`):
 *   - 204        the request is gone: the host is told (`onDeleted`) and shows that.
 *   - 409 `has_related_records`  the request already has activity (documents, messages, other records), so it cannot be
 *                deleted. The notice offers "Send recall request": a short box, prefilled and editable, that posts the text
 *                to the request's own thread through the EXISTING message post (`sendMessage`, the task 034 client) - there
 *                is no recall endpoint. A 201 closes the box and says the recall request was posted (the host re-reads the thread through
 *                `onRecalled`). A failure keeps the box and the text and says why (`sendErrorOf`, the thread's own wording).
 *   - 409 `service_request_closed`  the request is closed (staff closed it; owner decision 2026-10-12): it cannot be deleted
 *                and trying again will not help. Delete is not offered for a closed request (`isServiceRequestClosed`, from the
 *                `statecode` / `statuscode` the detail read now carries) and is withdrawn if the server says so after the click.
 *   - 503        nothing was deleted; the notice says to try again, and Delete is still there.
 *   - anything else: the shared `DeleteFailureNotice` wording (403 uniform refusal, 500, network).
 *
 * After a recall is posted the request has a message of its own, so the server would now refuse a delete (409) - the Delete
 * action is withdrawn and the confirmation stays.
 *
 * ADR-021: Fluent UI v9 + `makeStyles` + semantic tokens only; the shared modals own their own danger styling.
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
import { DeleteRegular } from '@fluentui/react-icons';
import { FormModal } from '@spaarke/ui-components/components/SprkModal';
import {
  REASON_HAS_RELATED_RECORDS,
  REASON_SERVICE_REQUEST_CLOSED,
  isRequestedByCaller,
  isServiceRequestClosed,
  type CallerIdentity,
} from '../../api/delete-own';
import { MAX_MESSAGE_BODY_LENGTH, sendMessage } from '../../api/messages-client';
import { deleteOwnServiceRequest } from '../../api/web-api-client';
import { BffAccessState } from '../auth/BffAccessState';
import { DeleteFailureNotice, DeleteOwnConfirm, useDeleteOwn } from '../DeleteOwn';
import { sendErrorOf, type SendError } from './RecordMessages';

/** The message route segment of a service request thread (`MESSAGE_ROOTS.sprk_servicerequest`). */
const SERVICE_REQUEST_MESSAGE_ROOT = 'servicerequests';

/** Prefilled recall text; the user may edit it. */
export const DEFAULT_RECALL_TEXT = 'Please recall this request.';

const RECALLED_TEXT = "Your recall request was posted to this request's messages for the legal team.";

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  actions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  recallBody: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  textarea: {
    width: '100%',
  },
  counter: {
    color: tokens.colorNeutralForeground3,
    alignSelf: 'flex-end',
  },
  counterOver: {
    color: tokens.colorPaletteRedForeground1,
  },
});

export interface ServiceRequestActionsProps {
  /** The service request's id (a GUID the detail view has already validated). */
  requestId: string;
  /** The request's record as read (its `sprk_requestedby` names the requester). */
  record: Record<string, unknown>;
  /** The signed-in caller's identity, or null while unknown (no action is offered). */
  caller: CallerIdentity | null;
  /** The name shown in the delete confirmation. */
  label: string;
  /** The server answered 204: the request no longer exists. */
  onDeleted: () => void;
  /** The recall message was posted (201): re-read the request's thread. */
  onRecalled: () => void;
  /** True while the recall post is in flight, so the host can hold paging and closing. */
  onBusyChange?: (busy: boolean) => void;
}

export const ServiceRequestActions: React.FC<ServiceRequestActionsProps> = ({
  requestId,
  record,
  caller,
  label,
  onDeleted,
  onRecalled,
  onBusyChange,
}) => {
  const styles = useStyles();
  const [recallOpen, setRecallOpen] = React.useState(false);
  const [recallText, setRecallText] = React.useState(DEFAULT_RECALL_TEXT);
  const [sending, setSending] = React.useState(false);
  const [recallError, setRecallError] = React.useState<SendError | null>(null);
  const [recalled, setRecalled] = React.useState(false);
  // Same-tick lock: the message post has no idempotency guard on the server, so a double click must send once.
  const sendLock = React.useRef(false);

  const del = useDeleteOwn<string>({
    noun: 'service request',
    remove: deleteOwnServiceRequest,
    onRemoved: onDeleted,
  });

  const onBusyChangeRef = React.useRef(onBusyChange);
  onBusyChangeRef.current = onBusyChange;
  React.useEffect(() => {
    onBusyChangeRef.current?.(sending);
  }, [sending]);
  React.useEffect(
    () => () => {
      onBusyChangeRef.current?.(false);
    },
    []
  );

  if (!isRequestedByCaller(record, caller)) return null;

  const text = recallText.trim();
  const tooLong = text.length > MAX_MESSAGE_BODY_LENGTH;
  const canSend = text.length > 0 && !tooLong && !sending;
  const hasActivity = del.failure?.kind === 'error' && del.failure.reasonCode === REASON_HAS_RELATED_RECORDS;
  // A closed request cannot be deleted (owner decision 2026-10-12): no Delete, whether the record said so up front
  // (statecode / statuscode) or the server's 409 service_request_closed just did (the read was stale).
  const closed =
    isServiceRequestClosed(record) ||
    (del.failure?.kind === 'error' && del.failure.reasonCode === REASON_SERVICE_REQUEST_CLOSED);
  // Recall is only ever offered after a has_related_records 409, which the server answers only for an open request, so
  // a closed request has no recall either.
  if (closed && !recalled && !del.failure) return null;

  const openRecall = (): void => {
    setRecallError(null);
    setRecallOpen(true);
  };

  const closeRecall = (): void => {
    // Cancel / x never discards an in-flight post.
    if (sendLock.current) return;
    setRecallOpen(false);
    setRecallError(null);
  };

  const sendRecall = async (): Promise<void> => {
    if (sendLock.current || !canSend) return;
    sendLock.current = true;
    setSending(true);
    setRecallError(null);
    try {
      await sendMessage(SERVICE_REQUEST_MESSAGE_ROOT, requestId, text);
    } catch (err: unknown) {
      // The box stays open with the text exactly as typed.
      setRecallError(sendErrorOf(err));
      sendLock.current = false;
      setSending(false);
      return;
    }
    sendLock.current = false;
    setSending(false);
    setRecallOpen(false);
    setRecalled(true);
    del.dismissFailure();
    onRecalled();
  };

  const overBy = text.length - MAX_MESSAGE_BODY_LENGTH;

  return (
    <div className={styles.root} data-testid="service-request-actions">
      {recalled ? (
        <MessageBar intent="success" role="status" data-testid="service-request-recalled">
          <MessageBarBody>{RECALLED_TEXT}</MessageBarBody>
        </MessageBar>
      ) : (
        !closed && (
          <div className={styles.actions}>
            <Button
              appearance="subtle"
              icon={<DeleteRegular />}
              disabled={del.busy}
              onClick={() => del.ask(requestId, label)}
            >
              Delete request
            </Button>
          </div>
        )
      )}

      <DeleteOwnConfirm del={del} />

      {hasActivity && !recalled ? (
        <MessageBar intent="warning" role="alert" data-testid="service-request-has-activity">
          <MessageBarBody>{del.failure?.kind === 'error' ? del.failure.message : ''}</MessageBarBody>
          <MessageBarActions>
            <Button appearance="secondary" size="small" onClick={openRecall}>
              Send recall request
            </Button>
          </MessageBarActions>
        </MessageBar>
      ) : (
        !recalled && <DeleteFailureNotice del={del} />
      )}

      <FormModal
        open={recallOpen}
        onClose={closeRecall}
        onSubmit={() => void sendRecall()}
        title="Send recall request"
        size="sm"
        submitLabel={sending ? 'Sending...' : 'Send'}
        submitDisabled={!canSend}
        busy={sending}
      >
        <div className={styles.recallBody}>
          <Text size={300}>
            This request already has activity, so it cannot be deleted. Ask legal to recall it instead. Your message is
            added to the request&apos;s messages.
          </Text>
          <Textarea
            className={styles.textarea}
            aria-label="Recall request message"
            resize="vertical"
            rows={3}
            value={recallText}
            disabled={sending}
            onChange={(_e, data) => setRecallText(data.value)}
          />
          <Text
            size={200}
            className={mergeClasses(styles.counter, tooLong && styles.counterOver)}
            role={tooLong ? 'alert' : undefined}
            data-testid="recall-counter"
          >
            {tooLong
              ? `${overBy.toLocaleString()} over the ${MAX_MESSAGE_BODY_LENGTH.toLocaleString()} character limit`
              : `${text.length.toLocaleString()} / ${MAX_MESSAGE_BODY_LENGTH.toLocaleString()}`}
          </Text>
          {sending && <Spinner size="tiny" label="Sending..." />}
          {recallError?.kind === 'signin' && <BffAccessState failure="signin" />}
          {recallError?.kind === 'text' && (
            <MessageBar intent="error" role="alert" data-testid="recall-error">
              <MessageBarBody>{recallError.text}</MessageBarBody>
            </MessageBar>
          )}
        </div>
      </FormModal>
    </div>
  );
};

export default ServiceRequestActions;
