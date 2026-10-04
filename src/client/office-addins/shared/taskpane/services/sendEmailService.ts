import { cleanGuid } from '../utils/cleanGuid';
import { buildOpenRecordUrl, configuredSpaarkeAppName } from './openRecordLauncher';
import { mintDocumentShareLink } from './shareLinkService';

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
 *
 * ---
 *
 * **Task 086 (FR-15 amended 2026-10-02): the Word choice.** `Office.context.mailbox` does not exist
 * in Word, so `canComposeEmail` is always `false` there (above). Per the owner's 2026-10-02 decision
 * (superseding the 2026-09-15 "hidden in Word" choice), Word instead offers the user TWO browser-tab
 * compose destinations, built by {@link prepareWordSendEmailChoice} and opened via
 * `openRecordLauncher.openUrlInBrowserWindow` (gated on `canOpenBrowserWindow`, never `canComposeEmail`
 * or `hostType` — NFR-10):
 *
 * - **(A) Spaarke email** — {@link buildSpaarkeComposeUrl}: the Communication code page
 *   (`sprk_communicationpage`) in `mode=compose`, opened exactly the way
 *   `createTodoLauncher.ts` opens `sprk_smarttodo` (`{orgUrl}/main.aspx?pagetype=webresource&
 *   webresourceName=...`), with the pre-fill carried in the SAME `data=` envelope
 *   `CommunicationPage/src/services/parseParams.ts` documents and `index.tsx` actually parses
 *   (`parseFromLocation(window.location.search)`) — verified by reading both files, not assumed.
 *   `associatedTo=<entityType>:<guid>` stamps the related record; absent one, it is simply omitted
 *   (the page's own parser treats it as optional) and the composer still opens. The body is HTML —
 *   `CommunicationLayout.tsx` passes `initialBody` straight into `EmailComposer`, whose reducer
 *   defaults `bodyFormat` to `'HTML'` when the URL contract carries no format of its own — so this
 *   reuses the SAME `buildComposeBody` HTML as the Outlook path, not a second body format.
 * - **(B) Outlook on the web** — {@link buildOutlookWebComposeUrl}: the documented
 *   `https://outlook.office.com/mail/deeplink/compose` deep link (`notes/036-send-email-via-outlook.md`
 *   option 1), PLAIN TEXT body (the deep link has no HTML body parameter) via
 *   {@link buildPlainTextComposeBody} — same two links, same labels, as plain `label: url` lines.
 *
 * Both reuse the EXACT SAME link-minting/building step `prepareSendEmail` uses (the document
 * share-link mint + `buildRecordLink`) — never a second way to mint a link. `prepareWordSendEmailChoice`
 * returns a `spaarkeUrl` of `null` when `ORG_URL` is unset (the Spaarke composer is itself a Dataverse
 * page and cannot be addressed without it) while `outlookWebUrl` is still built — mirrors the
 * "degrade, don't break" rule the record-link path already follows elsewhere in this file.
 *
 * {@link resolveSendEmailAffordance} is the single, pure, capability-only gating decision
 * (`'outlook-native' | 'word-choice' | 'none'`) `App.tsx` renders from — kept here, next to the two
 * orchestration functions it chooses between, so the gating rule has exactly one tested definition
 * rather than being re-derived inline in the component.
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

// The document link is minted by `shareLinkService.mintDocumentShareLink` (task 075): one minter for the Send
// Email path and the Word ribbon's Share command, the same route at its existing expiry policy.

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

  // Task 088: the record link names the Spaarke app (`SPAARKE_APP_NAME`), like every other record link the
  // add-in builds, so a recipient lands in Spaarke rather than in their own default app.
  return { url: buildOpenRecordUrl(orgUrl, record.entityType, id, configuredSpaarkeAppName()), label };
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

/** @internal A resolved link, or `null` when that source has nothing to link. */
type ResolvedLink = { url: string; label: string } | null;

/** @internal Outcome of {@link resolveSendEmailLinks} — mirrors the two non-'ready' `PrepareSendEmailResult` kinds. */
type ResolveLinksOutcome =
  | { kind: 'ready'; documentLink: ResolvedLink; recordLink: ResolvedLink }
  | { kind: 'nothing-to-send' }
  | { kind: 'error'; message: string };

/**
 * THE ONE link-resolution step for every Send Email destination (Outlook's native compose, task 036;
 * the Word choice's two browser-tab destinations, task 086): mints the document share link (when a
 * document is given) and builds the record deep link (when a related record is given and `orgUrl` is
 * configured). Shared by {@link prepareSendEmail} and {@link prepareWordSendEmailChoice} so there is
 * exactly one place that mints a link — never a second way, per this file's binding constraint.
 *
 * - Both present → both links returned.
 * - Only one present → that link only; the other is `null`, not an error.
 * - Neither present → `{ kind: 'nothing-to-send' }` (defensive; the UI should not have offered the
 *   action in this state).
 * - The document link mint fails → `{ kind: 'error' }`. This BLOCKS composition entirely, even when a
 *   record link is also available — the task's negative acceptance criterion is "no compose window
 *   opens with a broken or placeholder link", and silently downgrading to a record-only email would
 *   hide the fact that the user's actual request (send the document) failed.
 */
async function resolveSendEmailLinks(input: PrepareSendEmailInput): Promise<ResolveLinksOutcome> {
  const hasDocument = Boolean(input.document?.documentId && cleanGuid(input.document.documentId));
  const hasRelatedRecord = Boolean(input.relatedRecord?.id && cleanGuid(input.relatedRecord.id));

  if (!hasDocument && !hasRelatedRecord) {
    return { kind: 'nothing-to-send' };
  }

  let documentLink: ResolvedLink = null;
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

  return { kind: 'ready', documentLink, recordLink };
}

/**
 * Orchestrates a Send Email request for Outlook's native compose window (task 036): resolves the
 * links via {@link resolveSendEmailLinks} and composes the HTML body.
 */
export async function prepareSendEmail(input: PrepareSendEmailInput): Promise<PrepareSendEmailResult> {
  const resolved = await resolveSendEmailLinks(input);
  if (resolved.kind !== 'ready') {
    return resolved;
  }

  const subject = input.subject.trim() || DEFAULT_SUBJECT;
  return {
    kind: 'ready',
    content: { subject, htmlBody: buildComposeBody(resolved.documentLink, resolved.recordLink) },
  };
}

/**
 * Build the compose body as PLAIN TEXT — one `label: url` line per present link. Used by the Outlook
 * on-the-web deep link (task 086), whose `body=` query parameter has no HTML rendering (unlike
 * Outlook's native `displayNewMessageForm`, which accepts an `htmlBody`).
 */
function buildPlainTextComposeBody(documentLink: ResolvedLink, recordLink: ResolvedLink): string {
  const rows: string[] = [];
  if (documentLink) {
    rows.push(`${documentLink.label}: ${documentLink.url}`);
  }
  if (recordLink) {
    rows.push(`${recordLink.label}: ${recordLink.url}`);
  }
  return rows.join('\n');
}

/** Inputs for {@link buildSpaarkeComposeUrl}. */
export interface BuildSpaarkeComposeUrlInput {
  /** Dataverse org URL (`ORG_URL` env) — required; the Communication code page is a Dataverse web resource. */
  orgUrl: string;
  subject: string;
  /** HTML body — `CommunicationLayout`'s `initialBody` feeds `EmailComposer`, which defaults to HTML. */
  htmlBody: string;
  /** The related record to stamp via `associatedTo=<entityType>:<guid>`, or `null` to omit it. */
  associatedTo?: { entityType: string; id: string } | null;
}

/**
 * Build the URL that opens the Communication code page (`sprk_communicationpage`) in compose mode,
 * pre-filled — the SAME `main.aspx?pagetype=webresource&webresourceName=...` shape
 * `createTodoLauncher.ts` uses for `sprk_smarttodo`, extended with the Communication page's OWN
 * documented `data=` envelope contract (`CommunicationPage/src/services/parseParams.ts`,
 * `index.tsx`'s `parseFromLocation(window.location.search)`) rather than that page's flat-top-level
 * fallback — the envelope is what the page's own header comment documents as the real, exercised
 * contract (`Xrm.Navigation.navigateTo({ ..., data: "mode=view&id=<guid>" })`).
 *
 * **Why `data` is `encodeURIComponent`-wrapped before `URLSearchParams` assigns it (double-encode,
 * not single).** `parseParams.ts`'s `unwrapDataEnvelope` ALWAYS performs TWO decode passes on the
 * `data` value — the URL's own automatic one (`outer.get('data')`) and an unconditional, explicit
 * `decodeURIComponent` on top — regardless of how the value was produced. Assigning the envelope with
 * only `outer.searchParams.set('data', inner.toString())` (one encode pass) SURVIVES that only when no
 * field value contains a literal `&`/`=`: the extra decode pass prematurely un-escapes an encoded
 * `&`/`=` one step before the final `new URLSearchParams(...)` parse, which then reads it as a REAL
 * pair delimiter and truncates the value at that point. This URL's own embedded record link (itself a
 * multi-param `main.aspx?etn=...&id=...&pagetype=...&navbar=off` URL) and any "Smith & Jones"-style
 * subject both contain exactly that character, so the single-encode construction silently corrupts the
 * pre-fill. Explicitly pre-encoding with `encodeURIComponent(inner.toString())` here supplies the
 * matching SECOND encode pass the decode side performs — verified by a literal round-trip test driving
 * the exact `outer.get → decodeURIComponent → new URLSearchParams` sequence `parseParams.ts` runs
 * (`sendEmailService.test.ts`'s `decodeSpaarkeComposeUrl` helper), including a value containing `&`.
 */
export function buildSpaarkeComposeUrl(input: BuildSpaarkeComposeUrlInput): string {
  const inner = new URLSearchParams();
  inner.set('mode', 'compose');
  inner.set('subject', input.subject);
  inner.set('body', input.htmlBody);
  if (input.associatedTo) {
    inner.set('associatedTo', `${input.associatedTo.entityType}:${input.associatedTo.id}`);
  }

  const outer = new URL(`${input.orgUrl}/main.aspx`);
  outer.searchParams.set('pagetype', 'webresource');
  outer.searchParams.set('webresourceName', 'sprk_communicationpage');
  outer.searchParams.set('data', encodeURIComponent(inner.toString()));
  return outer.toString();
}

/** Inputs for {@link buildOutlookWebComposeUrl}. */
export interface BuildOutlookWebComposeUrlInput {
  subject: string;
  /** Plain text — see {@link buildPlainTextComposeBody}. */
  plainTextBody: string;
}

/**
 * Build the Outlook-on-the-web compose deep link (`notes/036-send-email-via-outlook.md` option 1):
 * `https://outlook.office.com/mail/deeplink/compose?subject=...&body=...`. No recipient is pre-filled —
 * the user addresses the message themselves, mirroring Outlook's native compose path.
 */
export function buildOutlookWebComposeUrl(input: BuildOutlookWebComposeUrlInput): string {
  const url = new URL('https://outlook.office.com/mail/deeplink/compose');
  url.searchParams.set('subject', input.subject);
  url.searchParams.set('body', input.plainTextBody);
  return url.toString();
}

/** Outcome of {@link prepareWordSendEmailChoice}. */
export type PrepareWordSendEmailResult =
  | {
      kind: 'ready';
      /** `null` when `ORG_URL` is unset — the Spaarke composer cannot be addressed without it. */
      spaarkeUrl: string | null;
      outlookWebUrl: string;
    }
  | { kind: 'nothing-to-send' }
  | { kind: 'error'; message: string };

/**
 * Orchestrates a Word Send Email request (task 086 / FR-15 amended 2026-10-02): resolves the SAME
 * links {@link prepareSendEmail} resolves (never a second way to mint one), then builds BOTH
 * destination URLs so the caller can open whichever the user picked from the choice. Building both
 * costs one extra, cheap URL-construction call — it does not mint a second document share link or
 * build a second record link.
 *
 * `spaarkeUrl` is `null` when `ORG_URL` is unset (mirrors `openRecordLauncher`'s / `SaveFlow.tsx`'s
 * `openRecordAvailable` "degrade, don't break" rule) — callers must not open a broken Spaarke
 * composer link in that case; `outlookWebUrl` is still built and usable regardless, since it never
 * depends on `ORG_URL`.
 */
export async function prepareWordSendEmailChoice(input: PrepareSendEmailInput): Promise<PrepareWordSendEmailResult> {
  const resolved = await resolveSendEmailLinks(input);
  if (resolved.kind !== 'ready') {
    return resolved;
  }

  const subject = input.subject.trim() || DEFAULT_SUBJECT;
  const { documentLink, recordLink } = resolved;

  const cleanRecordId = input.relatedRecord ? cleanGuid(input.relatedRecord.id) : '';
  const associatedTo =
    input.relatedRecord && cleanRecordId ? { entityType: input.relatedRecord.entityType, id: cleanRecordId } : null;

  const spaarkeUrl = input.orgUrl
    ? buildSpaarkeComposeUrl({
        orgUrl: input.orgUrl,
        subject,
        htmlBody: buildComposeBody(documentLink, recordLink),
        associatedTo,
      })
    : null;

  const outlookWebUrl = buildOutlookWebComposeUrl({
    subject,
    plainTextBody: buildPlainTextComposeBody(documentLink, recordLink),
  });

  return { kind: 'ready', spaarkeUrl, outlookWebUrl };
}

/** The three Send Email affordance shapes a host can offer — see {@link resolveSendEmailAffordance}. */
export type SendEmailAffordance = 'outlook-native' | 'word-choice' | 'none';

/**
 * THE single gating decision for Send Email (NFR-10: capability-only, never `hostType` /
 * `Office.context.host`):
 *
 * - `'outlook-native'` — `canComposeEmail` is true (Outlook, read mode): the existing single-button
 *   affordance, unchanged by task 086.
 * - `'word-choice'` — `canComposeEmail` is false AND `canOpenBrowserWindow` is true (Word): the new
 *   two-option choice (Spaarke email / Outlook on the web).
 * - `'none'` — neither capability holds, or there is nothing to link: no Send Email affordance at
 *   all (never rendered-and-disabled — the task's negative acceptance criterion).
 *
 * Kept as one pure, exported function (rather than re-derived inline in `App.tsx`) so the gating rule
 * has exactly one tested definition.
 */
export function resolveSendEmailAffordance(
  capabilities: { canComposeEmail: boolean; canOpenBrowserWindow: boolean },
  hasSomethingToLink: boolean
): SendEmailAffordance {
  if (!hasSomethingToLink) {
    return 'none';
  }
  if (capabilities.canComposeEmail) {
    return 'outlook-native';
  }
  if (capabilities.canOpenBrowserWindow) {
    return 'word-choice';
  }
  return 'none';
}
