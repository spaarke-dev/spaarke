import { cleanGuid } from '@spaarke/ui-components/guid';
import { buildOpenRecordUrl, configuredSpaarkeAppName } from './openRecordLauncher';

/**
 * sendEmailService.ts — spaarkeai-word-add-in-r1 task 036 (FR-15): Send Email via Outlook's native compose.
 *
 * Opens the host's own Outlook compose window pre-populated with a link to the open/filed document's Spaarke
 * record and a link to its related Spaarke record, where each exists. This module owns the PURE composition
 * logic (link building + HTML body) — `App.tsx` owns the capability gate (`canComposeEmail`, NFR-10) and the
 * actual `hostAdapter.composeNewEmail()` call.
 *
 * **Task 096 (UAT round 4, 2026-10-04) — no sharing link, anywhere.** The document link used to be a Graph
 * sharing link minted by `POST /api/documents/{id}/share-link`. Graph refuses `createLink` for files in
 * SharePoint Embedded containers ("This sharing scenario is not supported on CSP Container site." — the
 * owner's screenshot), so every Send Email failed. The document link is now the document's SPAARKE record
 * (`sprk_document`, opened in the Spaarke app) — built the same way as the related-record link, with no
 * server call, so it cannot fail. A recipient must be able to open Spaarke to follow it; to send the FILE
 * itself, Word users use the in-pane Email tab (`components/views/EmailView.tsx`), which attaches it.
 *
 * Task 096 also removed task 086's Word "Send Email" choice (Spaarke email page / Outlook on the web in a
 * browser tab): Word now has the Email tab instead, so this file serves Outlook's native compose only.
 *
 * **The record link** reuses `openRecordLauncher.buildOpenRecordUrl` (task 027) — the SAME
 * `main.aspx?...etn=...&id=...&pagetype=entityrecord` shape used everywhere the pane opens a record. Every id
 * is canonicalized via `cleanGuid` (ADR-044) first. When `ORG_URL` is unset, links are omitted (mirrors
 * `openRecordLauncher`'s own unset-`orgUrl` no-op) — never a broken link.
 */

/** The document side of a Send Email request. */
export interface SendEmailDocumentInput {
  /** `sprk_documentid`, any form — canonicalized via `cleanGuid` before use. */
  documentId: string;
  /** The document's name, when known — used as the link label. */
  name?: string | null;
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
  /** `ORG_URL` env — required to build any Spaarke link; unset degrades to "no link" (never a broken one). */
  orgUrl: string | undefined;
}

/** A successfully composed message, ready to hand to `hostAdapter.composeNewEmail()`. */
export interface SendEmailComposeContent {
  subject: string;
  htmlBody: string;
}

export type PrepareSendEmailResult =
  | { kind: 'ready'; content: SendEmailComposeContent }
  /** Nothing could be linked (no document/record, or `ORG_URL` unset). Callers gate the affordance so this
   *  is normally unreachable via the UI. */
  | { kind: 'nothing-to-send' };

const DEFAULT_SUBJECT = 'Document from Spaarke';

/** A Spaarke deep link with its human-readable label. */
export interface SpaarkeLink {
  url: string;
  label: string;
}

/**
 * Build a related-record deep link, reusing task 027's URL builder verbatim — never a second URL-building
 * shape. Returns `null` (a defined no-op, not an error) when `orgUrl` is unset or the record id is empty once
 * canonicalized, mirroring `openRecordLauncher.openRecord`'s own rule. Exported for the Email tab
 * (`paneEmailService.ts`), so the pane builds record links for email in exactly one place.
 */
export function buildRecordLink(orgUrl: string | undefined, record: SendEmailRelatedRecordInput): SpaarkeLink | null {
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

  // Task 088: the record link names the Spaarke app (`SPAARKE_APP_NAME`), like every other record link the
  // add-in builds, so a recipient lands in Spaarke rather than in their own default app.
  return { url: buildOpenRecordUrl(orgUrl, record.entityType, id, configuredSpaarkeAppName()), label };
}

/**
 * Build the link to the document's own Spaarke record (`sprk_document`) — task 096's replacement for the Graph
 * sharing link, which SharePoint Embedded refuses. Same builder as {@link buildRecordLink}; never a server call.
 */
export function buildDocumentRecordLink(
  orgUrl: string | undefined,
  document: SendEmailDocumentInput
): SpaarkeLink | null {
  return buildRecordLink(orgUrl, {
    entityType: 'sprk_document',
    id: document.documentId,
    typeLabel: 'Document',
    displayName: document.name ?? null,
  });
}

/** HTML-escape a value for interpolation into the compose body. Exported for the Email tab's body. */
export function escapeHtml(text: string): string {
  const entities: Record<string, string> = {
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;',
  };
  return text.replace(/[&<>"']/g, char => entities[char] || char);
}

/** One `<p><a href="...">label</a></p>` row; every value HTML-escaped. Exported for the Email tab's body. */
export function linkParagraph(link: SpaarkeLink): string {
  return `<p><a href="${escapeHtml(link.url)}">${escapeHtml(link.label)}</a></p>`;
}

/**
 * Orchestrates a Send Email request for Outlook's native compose window (task 036): builds the document's and
 * the related record's Spaarke links and composes the HTML body — one row per link that exists.
 */
export function prepareSendEmail(input: PrepareSendEmailInput): PrepareSendEmailResult {
  const documentLink =
    input.document?.documentId && cleanGuid(input.document.documentId)
      ? buildDocumentRecordLink(input.orgUrl, input.document)
      : null;
  const recordLink = input.relatedRecord ? buildRecordLink(input.orgUrl, input.relatedRecord) : null;

  if (!documentLink && !recordLink) {
    return { kind: 'nothing-to-send' };
  }

  const subject = input.subject.trim() || DEFAULT_SUBJECT;
  const rows = [documentLink, recordLink].filter((l): l is SpaarkeLink => l !== null).map(linkParagraph);
  return { kind: 'ready', content: { subject, htmlBody: rows.join('\n') } };
}

/** The Send Email affordance shapes a host can offer — see {@link resolveSendEmailAffordance}. */
export type SendEmailAffordance = 'outlook-native' | 'none';

/**
 * THE single gating decision for the Send Email BUTTON (NFR-10: capability-only, never `hostType`):
 *
 * - `'outlook-native'` — `canComposeEmail` is true (Outlook, read mode) and there is something to link.
 * - `'none'` — otherwise. Word reports `canComposeEmail: false`, so it never shows the button; since task 096
 *   Word emails a document from its own Email tab (gated on `canEmailFromPane`), not from this button.
 */
export function resolveSendEmailAffordance(
  capabilities: { canComposeEmail: boolean },
  hasSomethingToLink: boolean
): SendEmailAffordance {
  return hasSomethingToLink && capabilities.canComposeEmail ? 'outlook-native' : 'none';
}
