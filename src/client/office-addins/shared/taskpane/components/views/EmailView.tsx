import React, { useCallback, useMemo, useRef, useState } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  MessageBarActions,
} from '@fluentui/react-components';
import { MailRegular, OpenRegular, SaveRegular } from '@fluentui/react-icons';
// The SAME compose engine the Spaarke email page mounts (`EmailComposerSlot` → `SendEmailPage` → `EmailComposer`),
// through its pane wrapper `SendEmailPane` (ADR-045). Resolved by an exact webpack/jest alias to the shared
// source — see webpack.config.js; types from `shared/types/spaarke-send-email-pane.d.ts`.
import { SendEmailPane } from '@spaarke/ui-components/send-email-pane';
import { useAnnounce } from '../../hooks/useAnnounce';
import { openRecord } from '../../services/openRecordLauncher';
import type { SendEmailRelatedRecordInput } from '../../services/sendEmailService';
import type { ContactOption } from './CreateTodoView';
import {
  buildPaneEmailAssociations,
  buildPaneEmailAttachment,
  buildPaneEmailBody,
  buildPaneEmailSubject,
  createPaneAuthenticatedFetch,
  toRecipientLookupItems,
  type PaneEmailDocument,
} from '../../services/paneEmailService';

/**
 * EmailView — the Word pane's Email tab (spaarkeai-word-add-in-r1 task 096; owner UAT round 4 items 6-8).
 *
 * A THIN CONTAINER over the shared compose engine (`EmailComposer`, `@spaarke/ui-components`, mounted through
 * its pane wrapper `SendEmailPane`) — the owner's
 * binding rule is "for the email form we should use our shared UI components so it looks consistent". The
 * engine renders and owns everything the user sees in the form (Send + From, To/Cc/Bcc with directory search,
 * Subject, Attachments, Related to, the rich-text body) and performs the send through its own
 * `sendCommunication()` → the EXISTING `POST /api/communications/send`. This container only:
 *   - decides whether the form can be offered at all (the document must already be in Spaarke — an unsaved
 *     document has no `sprk_document` id to attach, so it offers "Save to Spaarke first" instead);
 *   - pre-fills it (subject from the document name; body = a short line + the Spaarke record link; the
 *     document as the attachment; the related record as the association);
 *   - locks the send to the user's own mailbox (`sendMode="user"`, OBO `/me/sendMail`, owner decision);
 *   - shows the outcome (sent + "Open in Spaarke" for the recorded communication; the server's reason on a
 *     refusal) and resets the form after a send.
 *
 * Why `SendEmailPane` and not one of the other wrappers: `SendEmailPage` locks `mount="page"` (window chrome with
 * a close ×, meaningless in a task pane) and exposes neither `sendMode` nor `onError`; `SendEmailDialog` is a
 * modal; `SendEmailStep` is a wizard step without `onSent`/`onError`. ADR-045's rule for a new mount is "a new
 * thin wrapper over the one engine" — `SendEmailPane` (task 096) is that wrapper, in the shared library.
 *
 * Word only: the tab is gated on `HostCapabilities.canEmailFromPane` (NFR-10 — never `hostType`). Outlook
 * keeps its native compose window.
 */

type SendEmailPaneProps = React.ComponentProps<typeof SendEmailPane>;
type ComposerSendError = Parameters<NonNullable<SendEmailPaneProps['onError']>>[0];

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  documentLine: {
    color: tokens.colorNeutralForeground3,
    overflowWrap: 'anywhere',
  },
});

export interface EmailViewProps {
  /** The open document as Spaarke knows it — absent until the document is saved to (or resolved in) Spaarke. */
  document?: PaneEmailDocument | null;
  /**
   * Whether the pane knows if the open document is already in Spaarke, when `document` is absent:
   * `'checking'` — resolution still running; `'unconfirmed'` — the check failed (error / access denied /
   * conflict / indeterminate), so the document MAY already be in Spaarke and must not be called "new";
   * omitted — the document is genuinely not in Spaarke yet.
   */
  identityStatus?: 'checking' | 'unconfirmed';
  /** The record the document is filed to — becomes the email's association and its body link. */
  relatedRecord?: SendEmailRelatedRecordInput | null;
  /** `ORG_URL` — for the body's record link and "Open in Spaarke". Unset degrades to no link, never a broken one. */
  orgUrl: string | undefined;
  /** BFF base URL, host only (no `/api`) — the engine appends `/api/communications/send`. */
  bffBaseUrl: string;
  /** The signed-in user's mailbox address, shown on the engine's "From:" line. */
  fromMailbox?: string;
  /** The pane's token getter. The token never reaches the shared engine (ADR-028) — only a fetch function does. */
  getAccessToken: () => Promise<string | null>;
  /** Invalidate the token cache before the one 401 retry. */
  clearTokenCache?: () => void;
  /** The add-in's contact search (task 091) — the recipient directory. */
  onSearchContacts: (query: string) => Promise<ContactOption[]>;
  /** `canOpenBrowserWindow` (NFR-10) — gates "Open in Spaarke". */
  canOpenRecord: boolean;
  /** Switch to the Save tab. */
  onGoToSave: () => void;
}

/** Only the open document is attached — no add-file / link-document / record-search affordances. */
const NO_ADDITIONAL_ATTACHMENT_SOURCES: NonNullable<SendEmailPaneProps['attachmentSources']> = [];

type SendOutcome =
  | { kind: 'sent'; communicationId: string }
  | { kind: 'sent-unrecorded' }
  | { kind: 'error'; message: string };

export const EmailView: React.FC<EmailViewProps> = ({
  document,
  identityStatus,
  relatedRecord,
  orgUrl,
  bffBaseUrl,
  fromMailbox,
  getAccessToken,
  clearTokenCache,
  onSearchContacts,
  canOpenRecord,
  onGoToSave,
}) => {
  const styles = useStyles();
  const { announce, liveRegion } = useAnnounce();
  const [outcome, setOutcome] = useState<SendOutcome | null>(null);
  // Bumped after every completed send: remounting the engine resets the form to its pre-filled state.
  const [composerKey, setComposerKey] = useState(0);
  const wasSendingRef = useRef(false);

  const resetComposer = useCallback(() => setComposerKey(k => k + 1), []);

  const authenticatedFetch = useMemo(
    () =>
      createPaneAuthenticatedFetch({
        getAccessToken,
        ...(clearTokenCache ? { clearTokenCache } : {}),
        observer: {
          onNotSent: message => {
            // No request was made, so nothing went out — safe to try again once signed in.
            const text = `Email not sent — ${message}`;
            setOutcome({ kind: 'error', message: text });
            announce(text, 'assertive');
          },
          onTransportError: message => {
            // No response at all: the server may or may not have sent it (a dropped connection after the send).
            const text = `Email may not have been sent — no response from Spaarke (${message}). Check your Sent Items before sending again.`;
            setOutcome({ kind: 'error', message: text });
            announce(text, 'assertive');
          },
          onSentWithoutRecord: () => {
            setOutcome({ kind: 'sent-unrecorded' });
            announce('Email sent, but Spaarke did not record it.', 'assertive');
            resetComposer();
          },
        },
      }),
    [getAccessToken, clearTokenCache, announce, resetComposer]
  );

  const handleSearchRecipients = useCallback(
    async (query: string) => toRecipientLookupItems(await onSearchContacts(query)),
    [onSearchContacts]
  );

  const handleSent = useCallback(
    (result: { communicationId: string }) => {
      setOutcome({ kind: 'sent', communicationId: result.communicationId });
      announce('Email sent.', 'polite');
      resetComposer();
    },
    [announce, resetComposer]
  );

  const handleError = useCallback(
    (err: ComposerSendError) => {
      // Status 0 = no server refusal: the request was never made, got no response, or got a 2xx without a
      // record id. The pane fetch's observer (above) already reported each of those with the right wording
      // ("not sent" / "may not have been sent" / "sent, but not recorded"); since 2026-10-09 the engine also
      // forwards them here (normalized to status 0), so leave the observer's outcome in place.
      if (err.status === 0) return;
      // The server's own reason (ProblemDetails `detail`) — never a bare status code.
      const text = `Email not sent: ${err.detail || err.message}`;
      setOutcome({ kind: 'error', message: text });
      announce(text, 'assertive');
    },
    [announce]
  );

  // A new send attempt clears the previous outcome, so a stale "sent"/"error" never sits above a fresh send.
  const handleStateChange = useCallback((state: { isSending: boolean }) => {
    if (state.isSending && !wasSendingRef.current) {
      setOutcome(null);
    }
    wasSendingRef.current = state.isSending;
  }, []);

  const documentId = document?.documentId ?? '';
  const prefill = useMemo(
    () =>
      document && documentId
        ? {
            subject: buildPaneEmailSubject(document),
            body: buildPaneEmailBody(document, relatedRecord, orgUrl),
            attachments: [buildPaneEmailAttachment(document)],
            associations: buildPaneEmailAssociations(relatedRecord, orgUrl),
          }
        : null,
    [document, documentId, relatedRecord, orgUrl]
  );

  const openCommunicationAvailable = canOpenRecord && Boolean(orgUrl);

  return (
    <div className={styles.container} role="region" aria-label="Email">
      {liveRegion}
      <div className={styles.header}>
        <MailRegular aria-hidden="true" />
        <Text as="h2" size={500} weight="semibold">
          Email this document
        </Text>
      </div>

      {outcome?.kind === 'sent' && (
        <MessageBar intent="success" layout="multiline" role="status">
          <MessageBarBody>
            <MessageBarTitle>Email sent</MessageBarTitle>
            Sent from your mailbox with the document attached, and recorded in Spaarke.
          </MessageBarBody>
          <MessageBarActions>
            {/* NFR-10: only when the host can open a browser tab AND ORG_URL is set — never a dead link. */}
            {openCommunicationAvailable && (
              <Button
                appearance="outline"
                size="small"
                icon={<OpenRegular />}
                onClick={() =>
                  openRecord({ orgUrl, entityType: 'sprk_communication', recordId: outcome.communicationId })
                }
              >
                Open in Spaarke
              </Button>
            )}
          </MessageBarActions>
        </MessageBar>
      )}

      {outcome?.kind === 'sent-unrecorded' && (
        <MessageBar intent="warning" layout="multiline" role="status">
          <MessageBarBody>
            <MessageBarTitle>Email sent, but not recorded</MessageBarTitle>
            It was sent from your mailbox and is in your Sent Items, but Spaarke could not record it against the record.
            Do not send it again.
          </MessageBarBody>
        </MessageBar>
      )}

      {outcome?.kind === 'error' && (
        <MessageBar intent="error" layout="multiline" role="alert">
          <MessageBarBody>{outcome.message}</MessageBarBody>
        </MessageBar>
      )}

      {identityStatus === 'checking' && !prefill ? (
        <Spinner size="small" label="Checking whether this document is in Spaarke…" />
      ) : identityStatus === 'unconfirmed' && !prefill ? (
        // The identity check failed — the document may already be in Spaarke, so never call it "new" (a save
        // from here could then create a duplicate). The Save tab owns the retry and the explicit choice.
        <MessageBar intent="warning" layout="multiline" role="status">
          <MessageBarBody>
            <MessageBarTitle>Could not confirm this document</MessageBarTitle>
            Spaarke could not confirm whether this document is already saved. Check it on the Save tab, then email it
            from here.
          </MessageBarBody>
          <MessageBarActions>
            <Button appearance="primary" size="small" icon={<SaveRegular />} onClick={onGoToSave}>
              Go to Save
            </Button>
          </MessageBarActions>
        </MessageBar>
      ) : !prefill ? (
        // An unsaved document has no `sprk_document` to attach — Send is not offered at all.
        <MessageBar intent="info" layout="multiline" role="status">
          <MessageBarBody>
            <MessageBarTitle>Save to Spaarke first</MessageBarTitle>
            This document is not in Spaarke yet. Save it, then email it from here — it is sent as an attachment.
          </MessageBarBody>
          <MessageBarActions>
            <Button appearance="primary" size="small" icon={<SaveRegular />} onClick={onGoToSave}>
              Save to Spaarke first
            </Button>
          </MessageBarActions>
        </MessageBar>
      ) : (
        <>
          <Text size={200} className={styles.documentLine}>
            Attached: {prefill.attachments[0]?.fileName}
          </Text>
          <SendEmailPane
            // Remounted (reset to the pre-fill) after each completed send, and if the document or its record
            // changes while the tab is open — the engine reads its initial values only at mount.
            key={`${composerKey}:${documentId}:${relatedRecord?.id ?? ''}`}
            mode="compose"
            sendMode="user"
            {...(fromMailbox ? { fromMailbox } : {})}
            // The server's outbound `.eml` archive is not part of this flow (and is off by default server-side).
            archiveToSpe={false}
            // Only the open document is attached; no add-file / link-document / record-search affordances.
            attachmentSources={NO_ADDITIONAL_ATTACHMENT_SOURCES}
            initialSubject={prefill.subject}
            initialBody={prefill.body}
            initialBodyFormat="HTML"
            initialAttachments={prefill.attachments}
            associations={prefill.associations}
            onSearchRecipients={handleSearchRecipients}
            authenticatedFetch={authenticatedFetch}
            bffBaseUrl={bffBaseUrl}
            onSent={handleSent}
            // This tab tells the user itself (the outcome MessageBar + live-region announcement), so the
            // composer's own "Email not sent" dialog is switched off — one report per failure.
            sendFailureDisplay="host"
            onError={handleError}
            onStateChange={handleStateChange}
          />
        </>
      )}
    </div>
  );
};

export default EmailView;
