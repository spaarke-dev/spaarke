// -----------------------------------------------------------------------------
// composeIdentity.ts — FR-07(b) (spaarkeai-compose-r7, task 010)
// -----------------------------------------------------------------------------
// The non-rotating **logical document id** for an unsaved Compose document, and its
// client-only persistence.
//
// PROBLEM (pre-R7): no id survives a widget re-mount / page reload for an unsaved doc.
//   - `transientKey` is minted fresh at every transient/draft mount door and never persisted.
//   - `speDriveItemId` is reset to '' on transient mounts.
//   - `sprkDocumentId` exists only AFTER first-Save promotion.
// So a re-mount rotates identity → FR-03 draft recovery orphans drafts and FR-07 client
// dedup has no stable key.
//
// SOLUTION: mint a `composeLogicalId` ONCE per logical document, persist it to a single
// "active draft" slot in client storage, and rehydrate it on the recovery path. This id is
// the SHARED key for the FR-03 draft-recovery store (task 040) and the FR-07 client dedup
// path (task 011). It is CLIENT-ONLY — it never reaches the server save/version contract
// (ADR-049 unchanged; NFR-03).
//
// STORAGE MODEL — single active-draft slot (localStorage): consistent with the existing
// single-slot Compose conduits (the visibility bridge is "single-slot, last-mounted instance
// wins", ComposeWorkspace.tsx). localStorage (not sessionStorage) so recovery survives a tab
// CLOSE + reopen, not only an in-tab reload — matching the owner's "never lose work on
// crash/close/nav" priority. Multi-Compose-tab disambiguation is an accepted limitation today
// (last active draft wins the slot); task 040 may refine the scope key if it becomes needed.
// Device-switch loss is an accepted client-only limitation (spec Owner Clarifications).
// -----------------------------------------------------------------------------

/** localStorage key holding the id of the currently-active unsaved Compose draft. */
export const COMPOSE_ACTIVE_DRAFT_ID_KEY = 'spaarke.compose.activeDraftId';

/**
 * Mint a stable logical id. `crypto.randomUUID()`-preferring with a jsdom/older-runtime
 * fallback (same shape as the workspace's `mintTransientKey` / `mintDocumentSessionId`).
 */
export function mintComposeLogicalId(): string {
  const c = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto;
  if (c?.randomUUID) {
    return c.randomUUID();
  }
  return `compose-lid-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
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
 * Read the persisted active-draft logical id, or null if none/unavailable. Used by the
 * recovery path (task 040 drives the content re-mount; this returns the id to reuse) and by
 * unit tests simulating a re-mount.
 */
export function recoverActiveComposeLogicalId(): string | null {
  const s = storage();
  if (!s) return null;
  try {
    const v = s.getItem(COMPOSE_ACTIVE_DRAFT_ID_KEY);
    return v && v.length > 0 ? v : null;
  } catch {
    return null;
  }
}

/** Best-effort persist of the active-draft logical id. Never throws. */
export function persistActiveComposeLogicalId(id: string): void {
  const s = storage();
  if (!s || !id) return;
  try {
    s.setItem(COMPOSE_ACTIVE_DRAFT_ID_KEY, id);
  } catch {
    // quota / disabled — best-effort only (recovery degrades to "no draft found").
  }
}

/**
 * Clear the active-draft slot. Called on first-Save promotion (the doc now has a real
 * `sprkDocumentId`/`speDriveItemId`, so its identity no longer comes from the transient
 * logical id) so a promoted document is never mis-recovered as an unsaved blank draft.
 */
export function clearActiveComposeLogicalId(): void {
  const s = storage();
  if (!s) return;
  try {
    s.removeItem(COMPOSE_ACTIVE_DRAFT_ID_KEY);
  } catch {
    // best-effort
  }
}

/**
 * Start a NEW logical document: mint a fresh id and persist it as the active draft
 * (replacing any prior slot value). Call at USER-INITIATED new-doc mount doors (Blank page,
 * Open template, Browse, assistant-upload, AI-draft seed) — each is a distinct new document
 * whose id must persist so a subsequent reload recovers THIS document.
 */
export function startNewComposeLogicalId(): string {
  const id = mintComposeLogicalId();
  persistActiveComposeLogicalId(id);
  return id;
}

/**
 * FR-07(a) (task 012): make a Save-New (fork) filename distinct BY CONSTRUCTION so the server's SPE
 * PUT-by-path (which defaults to replace/version semantics — a same-name PUT re-versions the existing
 * drive-item) cannot coalesce the fork onto the ORIGINAL's file. Inserts a short unique token derived
 * from the fork's fresh transient key (a per-fork UUID) before the extension:
 *   "Contract.docx" → "Contract (copy 3f2a1b).docx".
 * Collision-safe without any drive-listing round-trip (the duplicate window the escalation trigger
 * warns about) — every fork mints a fresh key, so every fork name is distinct. The client sends this as
 * the create-on-save `displayName`, so the server writes to a NON-existing path and Graph mints a
 * DISTINCT item (a real fork); the client shows the same name (no server round-trip to reconcile).
 */
export function uniquifyForkFileName(fileName: string | undefined, forkKey: string): string {
  const token =
    forkKey
      .replace(/[^a-z0-9]/gi, '')
      .slice(0, 6)
      .toLowerCase() || Date.now().toString(36).slice(-6);
  const base = fileName && fileName.trim().length > 0 ? fileName : 'Untitled document.docx';
  const dot = base.lastIndexOf('.');
  return dot > 0 ? `${base.slice(0, dot)} (copy ${token})${base.slice(dot)}` : `${base} (copy ${token})`;
}
