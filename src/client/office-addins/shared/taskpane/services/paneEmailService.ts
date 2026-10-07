import type * as React from 'react';
import type { SendEmailPane } from '@spaarke/ui-components/send-email-pane';
import { authenticatedJsonFetch } from '@shared/services/authenticatedJsonFetch';
import { cleanGuid } from '@spaarke/ui-components/guid';
import type { ContactOption } from '../components/views/CreateTodoView';
import {
  buildDocumentRecordLink,
  buildRecordLink,
  escapeHtml,
  linkParagraph,
  type SendEmailRelatedRecordInput,
} from './sendEmailService';

/**
 * paneEmailService.ts — spaarkeai-word-add-in-r1 task 096 (UAT round 4, items 6-8): the PURE wiring between
 * the Word pane's state and the shared compose engine (`EmailComposer`, `@spaarke/ui-components` — the SAME
 * engine the Spaarke email page mounts — through its pane wrapper `SendEmailPane`). The engine owns the form, the validation and the send
 * (`sendCommunication()` → the EXISTING `POST /api/communications/send`); this module only translates the
 * pane's document / related record / contact search / auth into the engine's props. No UI here.
 *
 * Types are derived from the wrapper's props (`React.ComponentProps<typeof SendEmailPane>`, declared by
 * `shared/types/spaarke-send-email-pane.d.ts`) so this file names each shared shape exactly once.
 */

type EmailComposerProps = React.ComponentProps<typeof SendEmailPane>;

/** One attachment as the engine tracks it (`IAttachmentItem`). */
export type PaneEmailAttachment = NonNullable<EmailComposerProps['initialAttachments']>[number];
/** One association as the engine sends it (`ICommunicationAssociation`). */
export type PaneEmailAssociation = NonNullable<EmailComposerProps['associations']>[number];
/** One recipient-search result as the engine's `RecipientField` consumes it (`ILookupItem`). */
export type PaneRecipientLookupItem = Awaited<
  ReturnType<NonNullable<EmailComposerProps['onSearchRecipients']>>
>[number];
/** The engine's injected transport (`AuthenticatedFetchFn`, ADR-028 — no token crosses into the shared lib). */
export type PaneAuthenticatedFetch = EmailComposerProps['authenticatedFetch'];

/** The open document as the Email tab knows it. */
export interface PaneEmailDocument {
  /** `sprk_documentid` (any form; canonicalized). */
  documentId: string;
  /** The Spaarke document name, when known (preferred for the subject). */
  documentName?: string | null;
  /** The file name incl. extension (e.g. `Agreement.docx`), when known. */
  fileName?: string | null;
}

const DEFAULT_SUBJECT = 'Document from Spaarke';
const BODY_INTRO = 'Please see the attached document.';

/** `Agreement.docx` → `Agreement`. Only strips a short trailing extension; a name without one is unchanged. */
function stripExtension(name: string): string {
  return name.replace(/\.[A-Za-z0-9]{1,5}$/, '');
}

/** The subject: the document's name (without extension) — owner item 6 ("prefilled from the document name"). */
export function buildPaneEmailSubject(document: PaneEmailDocument): string {
  const name = (document.documentName ?? '').trim() || stripExtension((document.fileName ?? '').trim());
  return name || DEFAULT_SUBJECT;
}

/**
 * The body: a short line plus the Spaarke RECORD link (the related record; the document's own record when the
 * document is filed to none) — never a sharing link (task 096: SharePoint Embedded refuses those). The file
 * itself travels as an attachment, not a link.
 */
export function buildPaneEmailBody(
  document: PaneEmailDocument,
  relatedRecord: SendEmailRelatedRecordInput | null | undefined,
  orgUrl: string | undefined
): string {
  const link = relatedRecord
    ? buildRecordLink(orgUrl, relatedRecord)
    : buildDocumentRecordLink(orgUrl, {
        documentId: document.documentId,
        name: document.documentName ?? document.fileName ?? null,
      });
  const rows = [`<p>${escapeHtml(BODY_INTRO)}</p>`];
  if (link) {
    rows.push(linkParagraph(link));
  }
  return rows.join('\n');
}

/**
 * The open document as the engine's one attachment. `selected: true` is explicit: the engine includes a
 * compose-mode `initialAttachments` item only when told to (it defaults them off outside `forward`).
 * `documentId` is what the engine sends as `attachmentDocumentIds` — the server reads the file and attaches it.
 * `sizeBytes` is unknown to the pane (0): the server enforces the real 150-file / 35 MB limits.
 */
export function buildPaneEmailAttachment(document: PaneEmailDocument): PaneEmailAttachment {
  const documentId = cleanGuid(document.documentId);
  const fileName = (document.fileName ?? '').trim() || (document.documentName ?? '').trim() || 'Document';
  return {
    id: `pane-document-${documentId}`,
    source: 'spe',
    fileName,
    sizeBytes: 0,
    documentId,
    selected: true,
  };
}

/**
 * The related record as the engine's association — owner item 7 ("the email should be associated to the same
 * record"). `entityType` is the Dataverse LOGICAL name (`sprk_matter`): the server maps `associations[0]` onto
 * the matching `sprk_regarding*` lookup by logical name. No related record → no association (an unfiled
 * document's email is still sent and recorded, just not regarding a record).
 */
export function buildPaneEmailAssociations(
  relatedRecord: SendEmailRelatedRecordInput | null | undefined,
  orgUrl: string | undefined
): PaneEmailAssociation[] {
  if (!relatedRecord) {
    return [];
  }
  const entityId = cleanGuid(relatedRecord.id);
  if (!entityId || !relatedRecord.entityType) {
    return [];
  }
  const link = buildRecordLink(orgUrl, relatedRecord);
  const entityName = relatedRecord.displayName ?? undefined;
  return [
    {
      entityType: relatedRecord.entityType,
      entityId,
      ...(entityName ? { entityName } : {}),
      ...(link ? { entityUrl: link.url } : {}),
    },
  ];
}

/**
 * The add-in's contact search (task 091, `GET /api/office/search/entities?type=Contact`) as recipient-search
 * results. A contact without an email cannot be a recipient, so it is not offered. `name` follows the
 * `ILookupItem` convention ("Full Name (email)") so the row shows the email (owner: tell same-named people
 * apart); `email` is the first-class value the engine commits.
 */
export function toRecipientLookupItems(contacts: ContactOption[]): PaneRecipientLookupItem[] {
  return contacts
    .filter(c => Boolean(c.email && c.email.trim()))
    .map(c => {
      const email = (c.email ?? '').trim();
      return { id: c.id, name: `${c.name} (${email})`, email };
    });
}

/** What the transport observed that the engine itself does not report to the host. */
export interface PaneEmailTransportObserver {
  /**
   * No request was made (no token for the first attempt), so the email was certainly NOT sent — distinct from
   * {@link onTransportError}, where the user must check Sent Items before trying again.
   */
  onNotSent: (message: string) => void;
  /** A request went out but no response came back (offline, dropped connection): the email MAY have been sent. */
  onTransportError: (message: string) => void;
  /**
   * The send route answered 2xx but without a `communicationId`: the BFF records the `sprk_communication`
   * row AFTER the Graph send and best-effort, so this means the email WAS sent but Spaarke did not record it.
   * The engine treats the missing id as a plain error and does not tell the host, so the pane learns it here
   * (otherwise the user would see nothing and might send the email twice).
   */
  onSentWithoutRecord: () => void;
}

export interface CreatePaneAuthenticatedFetchInput {
  /** The pane's token getter (`authService.getAccessToken`). */
  getAccessToken: () => Promise<string | null>;
  /** Invalidate the token cache before the single 401 retry (`authService.clearCache`). */
  clearTokenCache?: () => void;
  observer: PaneEmailTransportObserver;
}

const SEND_ROUTE_PATH = '/api/communications/send';

/** True for the send route itself, whatever query string or trailing slash the engine adds. */
function isSendRoute(url: string): boolean {
  try {
    return new URL(url, 'https://placeholder.invalid').pathname.replace(/\/+$/, '').endsWith(SEND_ROUTE_PATH);
  } catch {
    return false;
  }
}

/**
 * The engine's injected `authenticatedFetch` (ADR-028: the shared lib never sees a token). Attaches the pane's
 * bearer token via the package's shared `authenticatedJsonFetch` (one 401 retry with a fresh token), and
 * reports the two outcomes the engine does not surface — see {@link PaneEmailTransportObserver}.
 */
export function createPaneAuthenticatedFetch(input: CreatePaneAuthenticatedFetchInput): PaneAuthenticatedFetch {
  const getToken = async (): Promise<string> => {
    const token = await input.getAccessToken();
    if (!token) {
      throw new Error('You are not signed in to Spaarke.');
    }
    return token;
  };

  return async (url: string, init?: RequestInit): Promise<Response> => {
    let token: string;
    try {
      token = await getToken();
    } catch (err) {
      input.observer.onNotSent(err instanceof Error ? err.message : 'You are not signed in to Spaarke.');
      throw err;
    }

    let response: Response;
    try {
      response = await authenticatedJsonFetch(url, init ?? {}, token, {
        getRetryToken: getToken,
        ...(input.clearTokenCache ? { onBeforeRetry: input.clearTokenCache } : {}),
      });
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Could not reach Spaarke.';
      input.observer.onTransportError(message);
      throw err;
    }

    if (response.ok && isSendRoute(url)) {
      try {
        const payload = (await response.clone().json()) as Record<string, unknown>;
        if (!payload['communicationId'] && !payload['CommunicationId']) {
          input.observer.onSentWithoutRecord();
        }
      } catch {
        // A 2xx from the send route means the email went out; an unreadable body only means the pane cannot
        // learn the record id. Same outcome for the user as a missing id.
        input.observer.onSentWithoutRecord();
      }
    }
    return response;
  };
}
