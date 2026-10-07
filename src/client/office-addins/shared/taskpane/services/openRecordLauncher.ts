/**
 * openRecordLauncher.ts
 *
 * spaarkeai-word-add-in-r1 task 027 (FR-10): opens a Dataverse record from the pane via
 * `Office.context.ui.openBrowserWindow` (`OpenBrowserWindowApi` 1.1) — the mechanism Spike-2
 * (`projects/spaarkeai-word-add-in-r1/notes/spikes/spike-2-dialog-api.md`) recommended: Option 3,
 * read-only detail in-pane (task 026's `RelatedRecordCard`) plus a browser-tab escape hatch. Full
 * rationale + the branch-taken record: `projects/spaarkeai-word-add-in-r1/notes/027-record-open-mechanism.md`.
 *
 * Deliberately NOT the Office Dialog API. Spike-2 found the plain-browser-tab route has zero
 * remaining platform risk; both Dialog-API options ((1) an MDA form directly in the dialog, (2) a
 * Spaarke-hosted code page in the dialog) carry unresolved AMBER items the spike explicitly
 * declined to build against. This is not re-litigated here (spec.md ADR Tensions / ADR-050 Path C).
 *
 * No auth token travels with this call. The opened tab authenticates against Dataverse using the
 * browser's own AAD session (or prompts interactively) — exactly as it does when a user opens a
 * Dataverse link today, independent of `@spaarke/auth`'s BFF-scoped token entirely (Spike-2 §b,
 * item 2). ADR-028 / the `spaarke-sso-binding` invariants govern that BFF-scoped token; it is
 * NEVER read or passed here — no URL param, no window message, no MSAL construction in this file.
 *
 * URL shape reuses the existing "Quick Create" precedent (`App.tsx` `onQuickCreate`,
 * email-communication-solution-r4 task 072 / FR-25): config-driven `ORG_URL`, unset → a safe
 * no-op with a stated reason, never a broken window open. This does not invent a second
 * URL-building shape — it extends the SAME `main.aspx?etn=...&pagetype=entityrecord` pattern with
 * `id=` to open an EXISTING record rather than a blank create form (matches Spike-2's own probe
 * URL, `notes/spikes/spike-2-dialog-api.md` Probe 1).
 *
 * Every record GUID is canonicalized via `cleanGuid` (ADR-044) before it reaches the URL,
 * regardless of whether the caller's id was already clean — this is the point-of-use guarantee
 * ADR-044 requires for anything that builds a link.
 *
 * Shape follows the existing `createTodoLauncher.ts` precedent (pure URL builder + a thin,
 * injectable side-effecting wrapper) — see that file's header for the shared discipline. It is a
 * SEPARATE service, not an extension of `createTodoLauncher`: that launcher owns a create-form
 * payload and a `window.open` popup lifecycle; this one owns an EXISTING-record deep link and the
 * `OpenBrowserWindowApi` lifecycle Spike-2 selected. They share no state.
 *
 * **Task 086 (FR-10 amended 2026-10-02)**: the opened record is a FOCUSED page (`navbar=off`, command
 * bar kept) — see {@link buildOpenRecordUrl}'s doc comment. (Task 086 also added a generic
 * `openUrlInBrowserWindow` for its Word Send Email choice; task 096 replaced that choice with the Email tab and
 * removed the then-unused helper.)
 *
 * **Task 088 (UAT-1, `notes/042-uat-round3-2026-10-03.md` §3a)**: a link that names no app opens in the
 * user's DEFAULT model-driven app, which may not be Spaarke's. Every record link therefore names the Spaarke
 * user app by its UNIQUE name (`appname=`) — stable across environments, unlike the app GUID — taken from the
 * `SPAARKE_APP_NAME` build setting ({@link configuredSpaarkeAppName}; injected by `webpack.config.js` exactly
 * like `ORG_URL`, default `sprk_MatterManagement`). Empty → the link is built WITHOUT `appname` (the
 * behaviour before task 088), never with a guessed value.
 *
 * @see projects/spaarkeai-word-add-in-r1/spec.md FR-10
 * @see projects/spaarkeai-word-add-in-r1/notes/spikes/spike-2-dialog-api.md
 * @see projects/spaarkeai-word-add-in-r1/notes/027-record-open-mechanism.md
 * @see projects/spaarkeai-word-add-in-r1/notes/086-word-send-email-and-focused-open.md
 * @see projects/spaarkeai-word-add-in-r1/notes/088-save-tab-after-save.md
 */

import { cleanGuid } from '@spaarke/ui-components/guid';

/**
 * The Spaarke model-driven app's unique name, from the `SPAARKE_APP_NAME` build setting (task 088). An unset
 * or blank setting yields `''`, which {@link buildOpenRecordUrl} treats as "name no app". The default
 * (`sprk_MatterManagement`) lives in the build setting, not here — this function never invents one.
 */
export function configuredSpaarkeAppName(): string {
  return (process.env.SPAARKE_APP_NAME ?? '').trim();
}

/** Inputs for opening an existing Dataverse record from the pane. */
export interface OpenRecordInput {
  /**
   * Dataverse org URL (`ORG_URL` env, e.g. `https://contoso.crm.dynamics.com`). `undefined` or
   * empty degrades to a safe no-op — see {@link OpenRecordResult.reason}. Mirrors the Quick Create
   * precedent exactly: leaving `ORG_URL` unset disables the deep link rather than opening a
   * broken window.
   */
  orgUrl: string | undefined;
  /** Dataverse logical entity name, e.g. `sprk_matter`, `sprk_document`. */
  entityType: string;
  /** Record id in any form (braced, mixed-case) — canonicalized via `cleanGuid` before use. */
  recordId: string;
  /**
   * Task 088: the model-driven app to open the record in (its unique name). Omitted → the
   * `SPAARKE_APP_NAME` build setting ({@link configuredSpaarkeAppName}), so every caller names the Spaarke
   * app without having to thread it. `''` → no `appname` (the user's default app).
   */
  appName?: string;
}

/**
 * Outcome of an {@link openRecord} call — always a defined result, never a thrown exception for
 * an expected "not configured" / "no id" condition.
 */
export interface OpenRecordResult {
  /** `true` when a browser window/tab was actually opened. */
  opened: boolean;
  /** Present when `opened` is `false` — a human-readable reason. Also logged via `console.warn`. */
  reason?: string;
}

/**
 * Build the `main.aspx` deep-link URL for an existing record. Exported for tests; callers should
 * normally use {@link openRecord}, which also handles the unset-`orgUrl` / missing-id no-op cases
 * and performs the `cleanGuid` canonicalization.
 *
 * `navbar=off` (spaarkeai-word-add-in-r1 task 086, FR-10 amended 2026-10-02): the record opens as a
 * **focused record page** — the Spaarke app's own navigation is hidden, but the command bar is kept
 * (no `cmdbar=false`) so Save and the record's actions stay available. This differs deliberately from
 * the Quick Create URL in `App.tsx` (`onQuickCreate`), which sets BOTH `navbar=off` AND `cmdbar=false`
 * for its small popup create form — that URL is a separate, pre-existing builder and is out of this
 * task's scope.
 *
 * `appname=` (task 088): names the model-driven app the record opens in, by its unique name, URL-encoded.
 * A blank `appName` builds the link WITHOUT `appname` — the record then opens in the user's default app,
 * exactly as before task 088. Callers pass {@link configuredSpaarkeAppName} (or let {@link openRecord}
 * default to it).
 */
export function buildOpenRecordUrl(
  orgUrl: string,
  entityType: string,
  canonicalRecordId: string,
  appName = ''
): string {
  const app = appName.trim();
  const appParam = app ? `appname=${encodeURIComponent(app)}&` : '';
  return `${orgUrl}/main.aspx?${appParam}etn=${entityType}&id=${canonicalRecordId}&pagetype=entityrecord&navbar=off`;
}

/** The Console's web resource — Matter Management's "Work → Workspace" page (`notes/042-uat-round3-2026-10-03.md` §3a). */
export const SPAARKE_CONSOLE_WEB_RESOURCE = 'sprk_spaarkeai';

/**
 * Task 089 (UAT-10): the URL the Word ribbon's "Open Spaarke" opens — the Spaarke app at its Workspace (the
 * Console): `{orgUrl}/main.aspx?appname={appName}&pagetype=webresource&webresourceName=sprk_spaarkeai`.
 *
 * Built from the build settings only (`ORG_URL`, `SPAARKE_APP_NAME` — the same reader every record link uses,
 * {@link configuredSpaarkeAppName}); every query value is URL-encoded; no token, secret or BFF URL is ever part
 * of it. `null` when either setting is blank or `orgUrl` is not an absolute https URL — the command then says it
 * is not set up rather than opening a guessed or broken link (the pane's own ORG_URL rule, and §3a: "never a guess").
 */
export function buildOpenSpaarkeUrl(orgUrl: string | undefined, appName: string): string | null {
  const org = (orgUrl ?? '').trim().replace(/\/+$/, '');
  const app = appName.trim();
  if (!org || !app) {
    return null;
  }
  try {
    if (new URL(org).protocol !== 'https:') {
      return null;
    }
  } catch {
    return null;
  }
  return (
    `${org}/main.aspx?appname=${encodeURIComponent(app)}` +
    `&pagetype=webresource&webresourceName=${encodeURIComponent(SPAARKE_CONSOLE_WEB_RESOURCE)}`
  );
}

/**
 * Default opener: `Office.context.ui.openBrowserWindow` — the mechanism Spike-2 selected. Never
 * `window.open` / the Office Dialog API for this path. Injectable so tests can assert the call
 * without driving a live Office.js host.
 */
function defaultOpener(url: string): void {
  Office.context.ui.openBrowserWindow(url);
}

/**
 * Task 088 (UAT-5): opens an https file URL — the colliding document's `webUrl` from
 * `GET /api/documents/{id}/open-links` — by whichever of the two supported mechanisms the host has
 * (NFR-10: decided by capability, never `hostType`):
 *
 * - `canOpenBrowserWindow` → `Office.context.ui.openBrowserWindow` (desktop Word/Outlook), the same opener
 *   {@link openRecord} uses;
 * - otherwise → `window.open(url, '_blank')` (Office on the web, where `OpenBrowserWindowApi` is not
 *   supported). A `null` return means the browser blocked the window; that is reported, never swallowed.
 *
 * Only https: `openBrowserWindow` is documented to accept http/https only (Microsoft Learn, Office.UI;
 * OfficeDev/office-js#2820 closed "by design" for Office URI schemes), which is why the open-links
 * `desktopUrl` (`ms-word:…`) is not launched from the pane — see `notes/088-save-tab-after-save.md` §3.
 */
export function openFileUrl(
  url: string,
  canOpenBrowserWindow: boolean,
  openers: {
    browserWindow?: (url: string) => void;
    windowOpen?: (url: string, target: string) => Window | null;
  } = {}
): OpenRecordResult {
  if (canOpenBrowserWindow) {
    (openers.browserWindow ?? defaultOpener)(url);
    return { opened: true };
  }

  const opened = (openers.windowOpen ?? ((u: string, t: string) => window.open(u, t)))(url, '_blank');
  if (!opened) {
    const reason = 'Your browser blocked the new window. Allow pop-ups for this add-in and try again.';
    console.warn(`[Spaarke] Open file: ${reason}`);
    return { opened: false, reason };
  }
  // The opened page must not be able to script this pane (the noopener feature would hide the null check above).
  opened.opener = null;
  return { opened: true };
}

/**
 * Task 094 (UAT round 4 trial): launches `ms-word:` by a synthetic anchor click — the one mechanism
 * community evidence (OfficeDev/office-js#6926, Win Word 2608) showed launching desktop Word from a
 * task pane. **No SUPPORTED Office.js call can do this**: `openBrowserWindow` only accepts http/https
 * (Microsoft Learn, Office.UI; OfficeDev/office-js#2820 closed "by design" for Office URI schemes),
 * and a bare `window.open('ms-word:...')` from a task pane is undocumented. This is the owner-accepted
 * trial of that unsupported mechanism (2026-10-04: *"if this is just something to test, then fine"*).
 *
 * Nothing can detect a failed launch (no event, no promise rejection for a scheme the OS declines to
 * open) — this always reports `{ opened: true }`. "Open in browser" ({@link openFileUrl}) is the real
 * fallback button, never a retry of this one. Callers MUST gate the "Open in Word" affordance on
 * {@link HostCapabilities.canOpenDesktopWord} themselves (NFR-10) — this function does not re-check it.
 */
export function openDesktopUrl(
  url: string,
  click: (anchor: HTMLAnchorElement) => void = anchor => anchor.click()
): OpenRecordResult {
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  try {
    click(anchor);
  } finally {
    document.body.removeChild(anchor);
  }
  return { opened: true };
}

/**
 * Opens an existing Dataverse record in a new browser tab, following Spike-2's Option 3.
 *
 * Safe no-op (never throws, never opens a broken/blank window) when:
 * - `orgUrl` is unset — mirrors the Quick Create precedent exactly (`App.tsx` `onQuickCreate`).
 * - `recordId` is empty once canonicalized.
 *
 * Callers MUST gate the affordance itself on `HostCapabilities.canOpenBrowserWindow` (NFR-10) —
 * this function does not re-check the capability, so it should only be reachable from UI already
 * gated on it.
 */
export function openRecord(input: OpenRecordInput, opener: (url: string) => void = defaultOpener): OpenRecordResult {
  if (!input.orgUrl) {
    const reason = 'ORG_URL is not configured.';
    console.warn(`[Spaarke] Open record disabled: ${reason}`);
    return { opened: false, reason };
  }

  const id = cleanGuid(input.recordId);
  if (!id) {
    const reason = 'No record id was available to open.';
    console.warn(`[Spaarke] Open record disabled: ${reason}`);
    return { opened: false, reason };
  }

  const url = buildOpenRecordUrl(input.orgUrl, input.entityType, id, input.appName ?? configuredSpaarkeAppName());
  opener(url);
  return { opened: true };
}
