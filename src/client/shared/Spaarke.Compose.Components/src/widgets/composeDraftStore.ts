// -----------------------------------------------------------------------------
// composeDraftStore.ts — FR-03 (spaarkeai-compose-r7, task 040)
// -----------------------------------------------------------------------------
// The CLIENT-ONLY local draft store for draft-safe autosave. Persists a dirty
// Compose document's editor HTML to localStorage so unsaved work survives a
// crash / tab-close / navigation, and rehydrates it on reopen.
//
// BOUNDARY (NFR-03, the escalation trigger for task 040): this store NEVER writes
// to the BFF and NEVER creates an SPE version. Autosave = localStorage only; the
// SPE version is appended EXCLUSIVELY by an explicit Save (`triggerSave` →
// create-on-save / replace). The draft path and the server-save path are fully
// separate — this module imports nothing network-related and calls no fetch.
//
// KEY: the task-010 stable logical id (`getComposeLogicalIdentity` = first non-empty
// of `sprkDocumentId ?? speDriveItemId ?? composeLogicalId`). This is the SAME key
// the FR-07 client-dedup path and the `composeIdentity.ts` active-draft slot use —
// so a re-mount / reload rehydrates the correct draft rather than orphaning it. Do
// NOT derive a second key here.
//
// STORAGE MODEL — single active-draft-content slot (localStorage), consistent with
// `composeIdentity.ts`'s single `COMPOSE_ACTIVE_DRAFT_ID_KEY` slot: the newest dirty
// draft overwrites the slot, so the store never grows unbounded across many
// never-saved born-in-editor docs. `getComposeDraft(logicalId)` returns the entry
// only when its `logicalId` matches (so a stale slot from a different document is
// never mis-recovered). localStorage (not sessionStorage) so recovery survives a
// tab CLOSE + reopen, not only an in-tab reload — matching the owner's "never lose
// work on crash/close/nav" priority. Device-switch loss is an accepted client-only
// limitation (spec Owner Clarifications).
//
// The save-state indicator + `beforeunload` guard + the "no autosave" invariant
// comment/test flip are task 041, NOT here — this task is the store + recovery only.
// -----------------------------------------------------------------------------

/** localStorage key holding the single active unsaved Compose draft's content. */
export const COMPOSE_DRAFT_CONTENT_KEY = 'spaarke.compose.draftContent';

/** A persisted draft: the editor HTML plus the identity + metadata needed to recover it. */
export interface ComposeDraftEntry {
  /** The task-010 logical id this draft belongs to (the recovery match key). */
  logicalId: string;
  /** The editor HTML snapshot (TipTap `editor.getHTML()`). */
  html: string;
  /** The document name at draft time (seeds the recovered mount + name-on-save). */
  fileName?: string;
  /** ISO timestamp of when this draft was written (recency for the 041 recover-vs-server guard). */
  savedAt: string;
}

/** Best-effort localStorage handle — null on SSR / private-browsing / quota / disabled storage. */
function storage(): Storage | null {
  try {
    if (typeof window === 'undefined' || !window.localStorage) return null;
    return window.localStorage;
  } catch {
    // Accessing window.localStorage can THROW in some sandboxed/blocked contexts.
    return null;
  }
}

/**
 * Best-effort persist of a dirty draft's content for `logicalId`. Never throws (quota /
 * disabled storage degrade to "no draft found" on recovery). Overwrites the single slot.
 */
export function saveComposeDraft(logicalId: string, html: string, fileName?: string): void {
  const s = storage();
  if (!s || !logicalId) return;
  try {
    const entry: ComposeDraftEntry = {
      logicalId,
      html,
      fileName,
      savedAt: new Date().toISOString(),
    };
    s.setItem(COMPOSE_DRAFT_CONTENT_KEY, JSON.stringify(entry));
  } catch {
    // quota / disabled / serialization — best-effort only.
  }
}

/**
 * Read the persisted draft, but ONLY when it belongs to `logicalId` (so a slot left by a
 * different document is never mis-recovered). Returns null if none / mismatch / unparseable.
 */
export function getComposeDraft(logicalId: string): ComposeDraftEntry | null {
  const s = storage();
  if (!s || !logicalId) return null;
  try {
    const raw = s.getItem(COMPOSE_DRAFT_CONTENT_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<ComposeDraftEntry> | null;
    if (!parsed || parsed.logicalId !== logicalId || typeof parsed.html !== 'string') return null;
    return {
      logicalId,
      html: parsed.html,
      fileName: typeof parsed.fileName === 'string' ? parsed.fileName : undefined,
      savedAt: typeof parsed.savedAt === 'string' ? parsed.savedAt : '',
    };
  } catch {
    return null;
  }
}

/**
 * Clear the draft slot. Called on explicit-Save success so a promoted document is never
 * mis-recovered as an unsaved blank draft. When `logicalId` is given, clears ONLY when the
 * slot belongs to that id (so an unrelated document's draft is left intact); with no id it
 * clears unconditionally. Never throws.
 */
export function clearComposeDraft(logicalId?: string): void {
  const s = storage();
  if (!s) return;
  try {
    if (logicalId) {
      const raw = s.getItem(COMPOSE_DRAFT_CONTENT_KEY);
      if (raw) {
        const parsed = JSON.parse(raw) as Partial<ComposeDraftEntry> | null;
        if (parsed && parsed.logicalId !== logicalId) return; // different doc — leave it
      }
    }
    s.removeItem(COMPOSE_DRAFT_CONTENT_KEY);
  } catch {
    // best-effort
  }
}
