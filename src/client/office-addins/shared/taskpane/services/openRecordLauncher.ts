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
 * @see projects/spaarkeai-word-add-in-r1/spec.md FR-10
 * @see projects/spaarkeai-word-add-in-r1/notes/spikes/spike-2-dialog-api.md
 * @see projects/spaarkeai-word-add-in-r1/notes/027-record-open-mechanism.md
 */

import { cleanGuid } from '../utils/cleanGuid';

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
 */
export function buildOpenRecordUrl(orgUrl: string, entityType: string, canonicalRecordId: string): string {
  return `${orgUrl}/main.aspx?etn=${entityType}&id=${canonicalRecordId}&pagetype=entityrecord`;
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

  const url = buildOpenRecordUrl(input.orgUrl, input.entityType, id);
  opener(url);
  return { opened: true };
}
