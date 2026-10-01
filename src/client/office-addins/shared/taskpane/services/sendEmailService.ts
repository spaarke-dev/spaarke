import { apiClient, ApiClientError } from '@shared/services';
import { cleanGuid } from '../utils/cleanGuid';
import { buildOpenRecordUrl } from './openRecordLauncher';

/**
 * sendEmailService.ts — spaarkeai-word-add-in-r1 task 036 (FR-15): Send Email via Outlook.
 *
 * Opens the host's own Outlook compose window pre-populated with a link to the open/filed
 * document and a link to its related Spaarke record, where each exists. This module owns the
 * PURE composition logic (link minting orchestration + HTML body building) — `App.tsx` owns the
 * capability gate (`hostAdapter.getCapabilities().canComposeEmail`, NFR-10) and the actual
 * `hostAdapter.composeNewEmail()` call, since opening the host window is host-adapter territory,
 * not this service's.
 *
 * **Scope, binding (spec Scope / design.md §4.2)**: the Spaarke email-client modal variant is
 * explicitly deferred. This service NEVER renders a Spaarke UI for composing the message — it only
 * builds the subject/body handed to the host's native compose window.
 *
 * **The document link** is minted through the EXISTING `POST /api/documents/{documentId}/share-link`
 * route (`FileAccessEndpoints.cs`), at that route's existing expiry policy — no expiry override is
 * ever sent, and no alternative minting route is used. A minting failure is treated as blocking:
 * this service returns an `'error'` outcome and callers must not open a compose window at all,
 * per the task's negative acceptance criterion ("no compose window opens with a broken or
 * placeholder link").
 *
 * **The record link** reuses `openRecordLauncher.buildOpenRecordUrl` (task 027) — the SAME
 * `main.aspx?etn=...&id=...&pagetype=entityrecord` URL shape already used to open a record from the
 * pane, not a second URL-building shape. Every record id is canonicalized via `cleanGuid` (ADR-044)
 * before it reaches the URL. When `ORG_URL` is unset, the record link is simply omitted (mirrors
 * `openRecordLauncher`'s own unset-`orgUrl` no-op and `SaveFlow.tsx`'s `openRecordAvailable` rule) —
 * never a broken link, never a blocking error.
 */

/** The document side of a Send Email request. */
export interface SendEmailDocumentInput {
  /** `sprk_documentid`, any form — canonicalized via `cleanGuid` before use. */
  documentId: string;
}

/** The related-record side of a Send Email request (mirrors `ResolvedRelatedRecord`'s shape). */
export interface SendEmailRelatedRecordInput {
  /** Dataverse logical name, e.g. `sprk_matter`. */
  entityType: string;
  /** Record id, any form — canonicalized via `cleanGuid` before use. */
  id: string;
  /** Friendly type label for the link text, e.g. "Matter". Falls back to `entityType` when absent. */
  typeLabel?: string | null;
  /** The record's descriptive name, when known. */
  displayName?: string | null;
  /** The record's number, when known. */
  number?: string | null;
}

export interface PrepareSendEmailInput {
  /** The open/filed document, or `null`/`undefined` when there is none to link. */
  document?: SendEmailDocumentInput | null;
  /** The related Spaarke record, or `null`/`undefined` when there is none to link. */
  relatedRecord?: SendEmailRelatedRecordInput | null;
  /** The message subject (e.g. the open item's subject/title). Falls back to a generic subject when blank. */
  subject: string;
  /** `ORG_URL` env — required to build the record link; unset degrades to "no record link" (never a broken one). */
  orgUrl: string | undefined;
}

/** A successfully composed message, ready to hand to `hostAdapter.composeNewEmail()`. */
export interface SendEmailComposeContent {
  subject: string;
  htmlBody: string;
}

export type PrepareSendEmailResult =
  | { kind: 'ready'; content: SendEmailComposeContent }
  /** Neither a document nor a related record was available to link — nothing to send. Callers should
   *  gate the Send Email affordance's visibility so this is normally unreachable via the UI. */
  | { kind: 'nothing-to-send' }
  /** The document share link could not be minted. No compose window should be opened. */
  | { kind: 'error'; message: string };

const DEFAULT_SUBJECT = 'Document from Spaarke';

/** @internal Response shape from `POST /api/documents/{documentId}/share-link`. */
interface ShareLinkResponseBody {
  url: string;
  expiresAt: string;
  scope: string;
}

function shareLinkEndpoint(documentId: string): string {
  return `/api/documents/${documentId}/share-link`;
}

/**
 * Mint the document's recipient-openable share link via the EXISTING share-link route, at its
 * existing expiry policy. No request body is sent — omitting `allowExternalRecipients` keeps the
 * route's default (organization-scoped) behavior; this service never requests an expiry override or
 * external-recipient scope, and never mints a link through any other route.
 */
async function mintDocumentShareLink(
  documentId: string
): Promise<{ ok: true; url: string } | { ok: false; message: string }> {
  const id = cleanGuid(documentId);
  if (!id) {
    return { ok: false, message: 'No document id was available to link.' };
  }

  try {
    const response = await apiClient.post<ShareLinkResponseBody>(shareLinkEndpoint(id));
    return { ok: true, url: response.url };
  } catch (err) {
    if (err instanceof ApiClientError) {
      const status = err.error.status;
      if (status === 401 || status === 403) {
        return { ok: false, message: "You don't have permission to share this document." };
      }
      return { ok: false, message: err.error.detail || err.message || 'Could not create the document link.' };
    }
    return { ok: false, message: err instanceof Error ? err.message : 'Could not create the document link.' };
  }
}

/**
 * Build the related-record deep link, reusing task 027's URL builder verbatim — never a second
 * URL-building shape. Returns `null` (a defined no-op, not an error) when `orgUrl` is unset or the
 * record id is empty once canonicalized, mirroring `openRecordLauncher.openRecord`'s own rule.
 */
function buildRecordLink(
  orgUrl: string | undefined,
  record: SendEmailRelatedRecordInput
): { url: string; label: string } | null {
  if (!orgUrl) {
    return null;
  }
  const id = cleanGuid(record.id);
  if (!id) {
    return null;
  }

  const typeLabel = record.typeLabel || record.entityType;
  const label = record.displayName
    ? record.number
      ? `${typeLabel}: ${record.displayName} (${record.number})`
      : `${typeLabel}: ${record.displayName}`
    : `${typeLabel} record`;

  return { url: buildOpenRecordUrl(orgUrl, record.entityType, id), label };
}

function escapeHtml(text: string): string {
  const entities: Record<string, string> = {
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;',
  };
  return text.replace(/[&<>"']/g, char => entities[char] || char);
}

/**
 * Build the compose body's HTML. Includes a link row for each of `documentLink`/`recordLink` that is
 * present — one when only one link exists, both when both do. Every value is HTML-escaped; nothing
 * here trusts `displayName`/`typeLabel` to already be safe for interpolation.
 */
function buildComposeBody(
  documentLink: { url: string; label: string } | null,
  recordLink: { url: string; label: string } | null
): string {
  const rows: string[] = [];
  if (documentLink) {
    rows.push(`<p><a href="${escapeHtml(documentLink.url)}">${escapeHtml(documentLink.label)}</a></p>`);
  }
  if (recordLink) {
    rows.push(`<p><a href="${escapeHtml(recordLink.url)}">${escapeHtml(recordLink.label)}</a></p>`);
  }
  return rows.join('\n');
}

/**
 * Orchestrates a Send Email request: mints the document link (when a document is given), builds the
 * record link (when a related record is given and `orgUrl` is configured), and composes the message.
 *
 * - Both present → both links in the body.
 * - Only one present → that link only; the other is silently omitted, not an error.
 * - Neither present → `{ kind: 'nothing-to-send' }` (defensive; the UI should not have offered the
 *   action in this state).
 * - The document link mint fails → `{ kind: 'error' }`. This BLOCKS composition entirely, even when a
 *   record link is also available — the task's negative acceptance criterion is "no compose window
 *   opens with a broken or placeholder link", and silently downgrading to a record-only email would
 *   hide the fact that the user's actual request (send the document) failed.
 */
export async function prepareSendEmail(input: PrepareSendEmailInput): Promise<PrepareSendEmailResult> {
  const hasDocument = Boolean(input.document?.documentId && cleanGuid(input.document.documentId));
  const hasRelatedRecord = Boolean(input.relatedRecord?.id && cleanGuid(input.relatedRecord.id));

  if (!hasDocument && !hasRelatedRecord) {
    return { kind: 'nothing-to-send' };
  }

  let documentLink: { url: string; label: string } | null = null;
  if (hasDocument && input.document) {
    const minted = await mintDocumentShareLink(input.document.documentId);
    if (!minted.ok) {
      return { kind: 'error', message: minted.message };
    }
    documentLink = { url: minted.url, label: 'Open document' };
  }

  const recordLink =
    hasRelatedRecord && input.relatedRecord ? buildRecordLink(input.orgUrl, input.relatedRecord) : null;

  if (!documentLink && !recordLink) {
    // Only reachable when the sole available link source was a related record whose link could not
    // be built (e.g. ORG_URL unset) — a defined "nothing to send" outcome, not a mint failure.
    return { kind: 'nothing-to-send' };
  }

  const subject = input.subject.trim() || DEFAULT_SUBJECT;
  return {
    kind: 'ready',
    content: { subject, htmlBody: buildComposeBody(documentLink, recordLink) },
  };
}
