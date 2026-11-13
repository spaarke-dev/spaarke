/**
 * composeOnlySend.ts - the email a compose-only composer collected, and its send once the host's record exists
 * (spaarke-ontology-platform-r1 task 182, owner D-219).
 *
 * A create wizard's Send Email step mounts `EmailComposer` compose-only: the record the email is filed under does not exist
 * while the step is shown, and the wizard shell unmounts the step on Next. The step therefore lifts the composer's fields into
 * the wizard as an {@link IComposedEmail}, and the wizard's Finish sends it with {@link sendComposedEmail}.
 *
 * {@link sendComposedEmail} is the composer's own request path, not a second one: it runs the same steps as
 * `EmailComposer`'s `send()` — `resolveAttachmentShareLinks` + `findUnresolvedLinks`, then `mapStateToSendRequest` over
 * composer state, then `sendCommunication()` (`POST /api/communications/send`) — so From (`sendMode`), To, Cc, Bcc,
 * attachments, body format and `archiveToSpe` reach the BFF exactly as the Send Email dialog sends them. The only difference
 * is `associations`, which the host supplies once the record exists.
 *
 * No Xrm (ADR-012); `authenticatedFetch` is injected (ADR-028).
 */
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import { sendCommunication, SendCommunicationError } from '../../services/communicationApi';
import type { CommunicationSendMode, ICommunicationAssociation } from '../../services/communicationApi';
import { EMAIL_RE, initialState, mapStateToSendRequest } from './EmailComposer.reducer';
import { findUnresolvedLinks, resolveAttachmentShareLinks } from './EmailComposer';
import { toPlainText } from './composeOnlyText';
import type {
  EmailComposerBodyFormat,
  EmailComposerState,
  IAttachmentItem,
  IEmailComposerProps,
  IRecipient,
} from './EmailComposer.types';

/** The fields a compose-only composer collected (its state, minus the send/UI flags). */
export interface IComposedEmail {
  to: IRecipient[];
  cc: IRecipient[];
  bcc: IRecipient[];
  subject: string;
  body: string;
  bodyFormat: EmailComposerBodyFormat;
  attachments: IAttachmentItem[];
  /** The From choice: the user's mailbox or the Spaarke shared mailbox. */
  sendMode: CommunicationSendMode;
  fromMailbox?: string;
}

/**
 * Nothing composed yet. Plain text and the shared mailbox: what the wizards' Finish sent before task 182, so a wizard whose
 * user never touches From sends as before.
 */
export const EMPTY_COMPOSED_EMAIL: IComposedEmail = {
  to: [],
  cc: [],
  bcc: [],
  subject: '',
  body: '',
  bodyFormat: 'PlainText',
  attachments: [],
  sendMode: 'sharedMailbox',
};

/**
 * The host-built composer handlers a compose-only step can use: the recipient side pane, record lookup, local file upload,
 * the template picker and AI drafting (`createXrmEmailComposeHandlers` builds them in a Dataverse host). Each one that is
 * missing hides its feature, as in every composer host.
 */
export type IEmailComposeHandlers = Pick<
  IEmailComposerProps,
  | 'onLookupRecipients'
  | 'recordLookupCatalog'
  | 'onLookupRecord'
  | 'onUploadLocalAttachment'
  | 'onResolveShareLink'
  | 'onListEmailTemplates'
  | 'onRenderEmailTemplate'
  | 'onDraftWithAi'
>;

/** The composed email in composer state, as the step lifts it on every change. */
export function composedEmailFromState(state: EmailComposerState): IComposedEmail {
  return {
    to: state.to,
    cc: state.cc,
    bcc: state.bcc,
    subject: state.subject,
    body: state.body,
    bodyFormat: state.bodyFormat,
    attachments: state.attachments,
    sendMode: state.sendMode,
    fromMailbox: state.fromMailbox,
  };
}

/** Kept attachments with no `sprk_document` yet (an upload still running, or a pick this host cannot upload). */
export function pendingAttachments(email: IComposedEmail): IAttachmentItem[] {
  return email.attachments.filter(a => a.selected !== false && !a.documentId);
}

/** Recipients (To, Cc or Bcc) whose address is not one. */
export function invalidRecipients(email: IComposedEmail): IRecipient[] {
  return [...email.to, ...email.cc, ...email.bcc].filter(r => !EMAIL_RE.test(r.email.trim()));
}

/**
 * True when the email can be sent at Finish: at least one To, every address valid, a subject and a body, and every kept
 * attachment uploaded (otherwise the send would leave it out without saying so).
 */
export function isComposedEmailReady(email: IComposedEmail): boolean {
  return (
    email.to.length > 0 &&
    invalidRecipients(email).length === 0 &&
    email.subject.trim() !== '' &&
    toPlainText(email.body, email.bodyFormat).trim() !== '' &&
    pendingAttachments(email).length === 0
  );
}

/** Addresses joined with "; " (the shape the wizards' older string fields used). */
export function joinRecipientAddresses(recipients: readonly IRecipient[]): string {
  return recipients
    .map(r => r.email.trim())
    .filter(Boolean)
    .join('; ');
}

export interface ISendComposedEmailOptions {
  /** The record(s) the email is filed under: the record the wizard just created. */
  associations: ICommunicationAssociation[];
  authenticatedFetch: AuthenticatedFetchFn;
  /** Host only, no `/api`. */
  bffBaseUrl?: string;
  /** The host's record-link resolver for documents the user chose to link (same handler the composer uses). */
  onResolveShareLink?: (documentId: string) => Promise<string | null>;
}

export interface ISendComposedEmailResult {
  /** True when the email was sent, or when there was nothing to send (no To). */
  success: boolean;
  communicationId?: string;
  /** Why it was not sent, in the user's words. Set when `success` is false. */
  warning?: string;
}

function failureDetail(err: unknown): string {
  if (err instanceof SendCommunicationError) {
    return err.correlationId ? `${err.detail} (reference ${err.correlationId})` : err.detail;
  }
  return err instanceof Error ? err.message : 'unknown error';
}

/**
 * Send the composed email through the composer's request path, filed under `associations`. Never throws: a failure is
 * returned as a warning, so the caller reports it and keeps the record it created. No To: nothing is sent (`success`).
 */
export async function sendComposedEmail(
  email: IComposedEmail,
  options: ISendComposedEmailOptions
): Promise<ISendComposedEmailResult> {
  if (email.to.length === 0) return { success: true };

  // An attachment with no document would be left out of the request without a word: refuse instead.
  const pending = pendingAttachments(email);
  if (pending.length > 0) {
    return {
      success: false,
      warning: `The email was not sent: ${pending.map(a => a.fileName).join(', ')} did not finish uploading.`,
    };
  }

  const state: EmailComposerState = {
    ...initialState({
      mode: 'compose',
      mount: 'inline',
      authenticatedFetch: options.authenticatedFetch,
      associations: options.associations,
    }),
    to: email.to,
    cc: email.cc,
    bcc: email.bcc,
    subject: email.subject,
    body: email.body,
    bodyFormat: email.bodyFormat,
    attachments: email.attachments,
    sendMode: email.sendMode,
    fromMailbox: email.fromMailbox,
  };

  try {
    // Same order as EmailComposer.send(): resolve linked documents' record links, refuse a link that cannot be built.
    const attachments = await resolveAttachmentShareLinks(state.attachments, options.onResolveShareLink);
    const unresolved = findUnresolvedLinks(attachments);
    if (unresolved.length > 0) {
      return {
        success: false,
        warning: `The email was not sent: the Spaarke record link for ${unresolved.map(a => a.fileName).join(', ')} could not be built here.`,
      };
    }
    const result = await sendCommunication(mapStateToSendRequest({ ...state, attachments }), {
      authenticatedFetch: options.authenticatedFetch,
      bffBaseUrl: options.bffBaseUrl,
    });
    return { success: true, communicationId: result.communicationId };
  } catch (err) {
    return { success: false, warning: `The email was not sent: ${failureDetail(err)}.` };
  }
}
