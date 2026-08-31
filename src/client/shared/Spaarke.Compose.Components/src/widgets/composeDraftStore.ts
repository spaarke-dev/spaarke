// -----------------------------------------------------------------------------
// composeDraftStore.ts — FR-03 (spaarkeai-compose-r7, task 040)
//                        FR-S09 item 8 (spaarkeai-compose-r8, task 016)
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
// STORAGE MODEL — ONE SLOT PER DOCUMENT (r8 task 016, FR-S09 item 8).
//
// This store used to write a SINGLE global slot, and its own comment described the
// consequence as a feature: "the newest dirty draft overwrites the slot". It is not
// a feature. With two Compose documents open, the second one's ~15s autosave tick
// DESTROYS the first one's unsaved work, and the recovery read then reports "no
// draft" — because the slot's `logicalId` no longer matches — for work that existed
// thirty seconds earlier. Silent, total, and indistinguishable from never having
// drafted at all. Drafts are now keyed `spaarke.compose.draftContent:{logicalId}`.
//
// The original design's real concern — unbounded growth across many never-saved
// born-in-editor docs — is answered directly instead of by collision:
// `MAX_RETAINED_DRAFTS` most-recent entries are kept and older ones are pruned on
// every write. Bounded, and bounded by age rather than by whichever document the
// user happened to touch last.
//
// MIGRATION of the legacy single slot: `COMPOSE_DRAFT_CONTENT_KEY` is still READ on
// recovery (gated on its `logicalId` matching, exactly as before), so a draft written
// by the previous build is recovered rather than discarded — the user does not lose
// work to a deploy. It is never WRITTEN again, and it is removed once the document it
// belongs to writes or clears its own slot. No migration sweep runs at load: a
// best-effort store must not do bulk work on a path the user is waiting on.
//
// localStorage (not sessionStorage) so recovery survives a tab CLOSE + reopen, not
// only an in-tab reload — matching the owner's "never lose work on crash/close/nav"
// priority. Device-switch loss is an accepted client-only limitation (spec Owner
// Clarifications).
//
// The save-state indicator + `beforeunload` guard + the "no autosave" invariant
// comment/test flip are task 041, NOT here — this task is the store + recovery only.
// -----------------------------------------------------------------------------

/**
 * LEGACY localStorage key — the single global draft slot used before r8 task 016.
 *
 * Retained for READ-ONLY recovery of a draft written by an earlier build (and by the
 * tests that assert that recovery). Nothing writes it any more; prefer
 * {@link composeDraftKey}.
 */
export const COMPOSE_DRAFT_CONTENT_KEY = 'spaarke.compose.draftContent';

/** Prefix for the per-document draft slots. The suffix is the task-010 logical id. */
const DRAFT_KEY_PREFIX = 'spaarke.compose.draftContent:';

/**
 * How many per-document drafts to retain. Older entries are pruned on write, oldest
 * first, so the store stays bounded without documents colliding with each other. Ten
 * covers "every Compose document a person has open or recently closed" with room to
 * spare; the tail is work already abandoned days ago.
 */
const MAX_RETAINED_DRAFTS = 10;

/** The per-document localStorage key for `logicalId`. */
export function composeDraftKey(logicalId: string): string {
  return `${DRAFT_KEY_PREFIX}${logicalId}`;
}

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

/** Parse + validate a stored slot. Returns null on anything unexpected — never throws. */
function parseEntry(raw: string | null, expectedLogicalId: string): ComposeDraftEntry | null {
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as Partial<ComposeDraftEntry> | null;
    if (!parsed || parsed.logicalId !== expectedLogicalId || typeof parsed.html !== 'string') return null;
    return {
      logicalId: expectedLogicalId,
      html: parsed.html,
      fileName: typeof parsed.fileName === 'string' ? parsed.fileName : undefined,
      savedAt: typeof parsed.savedAt === 'string' ? parsed.savedAt : '',
    };
  } catch {
    return null;
  }
}

/** Every per-document draft key currently in storage. Never throws. */
function draftKeys(s: Storage): string[] {
  const keys: string[] = [];
  try {
    for (let i = 0; i < s.length; i += 1) {
      const key = s.key(i);
      if (key && key.startsWith(DRAFT_KEY_PREFIX)) keys.push(key);
    }
  } catch {
    // Enumeration can throw in blocked contexts — treat as "nothing to prune".
  }
  return keys;
}

/**
 * Keep the {@link MAX_RETAINED_DRAFTS} most recently written drafts and drop the rest.
 *
 * Entries with an unparseable or missing `savedAt` sort oldest, so corrupt slots are the
 * first thing evicted. Best-effort throughout: a prune failure must never cost the caller
 * the draft it just wrote.
 */
function pruneOldDrafts(s: Storage, keepKey: string): void {
  try {
    const keys = draftKeys(s);
    if (keys.length <= MAX_RETAINED_DRAFTS) return;

    const scored = keys.map(key => {
      let savedAt = 0;
      try {
        const parsed = JSON.parse(s.getItem(key) ?? 'null') as { savedAt?: unknown } | null;
        if (typeof parsed?.savedAt === 'string') {
          const t = Date.parse(parsed.savedAt);
          if (!Number.isNaN(t)) savedAt = t;
        }
      } catch {
        // Unparseable → savedAt 0 → evicted first.
      }
      return { key, savedAt };
    });

    scored.sort((a, b) => b.savedAt - a.savedAt);
    for (const { key } of scored.slice(MAX_RETAINED_DRAFTS)) {
      // Never evict the draft this write just persisted, whatever its timestamp says.
      if (key !== keepKey) s.removeItem(key);
    }
  } catch {
    // best-effort
  }
}

/**
 * Best-effort persist of a dirty draft's content for `logicalId`. Never throws (quota /
 * disabled storage degrade to "no draft found" on recovery).
 *
 * Writes this document's OWN slot — a concurrent Compose document's draft is untouched
 * (FR-S09 item 8). Also retires the legacy global slot when it belonged to this document,
 * so recovery can never read a stale copy of the same draft.
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
    s.setItem(composeDraftKey(logicalId), JSON.stringify(entry));

    if (parseEntry(s.getItem(COMPOSE_DRAFT_CONTENT_KEY), logicalId)) {
      s.removeItem(COMPOSE_DRAFT_CONTENT_KEY);
    }

    pruneOldDrafts(s, composeDraftKey(logicalId));
  } catch {
    // quota / disabled / serialization — best-effort only.
  }
}

/**
 * Read the persisted draft for `logicalId`. Returns null if none / mismatch / unparseable.
 *
 * Falls back to the LEGACY global slot (still gated on a `logicalId` match, so another
 * document's draft is never mis-recovered) so a draft written before r8 task 016 survives
 * the deploy that introduced per-document keys.
 */
export function getComposeDraft(logicalId: string): ComposeDraftEntry | null {
  const s = storage();
  if (!s || !logicalId) return null;
  try {
    return (
      parseEntry(s.getItem(composeDraftKey(logicalId)), logicalId) ??
      parseEntry(s.getItem(COMPOSE_DRAFT_CONTENT_KEY), logicalId)
    );
  } catch {
    return null;
  }
}

/**
 * Clear a draft. Called on explicit-Save success so a promoted document is never
 * mis-recovered as an unsaved blank draft.
 *
 * With `logicalId`: clears that document's slot, plus the legacy global slot when it
 * belongs to that document — an unrelated document's draft is left intact. With no id:
 * clears every draft slot (the deliberate "forget all local drafts" action). Never throws.
 */
export function clearComposeDraft(logicalId?: string): void {
  const s = storage();
  if (!s) return;
  try {
    if (logicalId) {
      s.removeItem(composeDraftKey(logicalId));
      if (parseEntry(s.getItem(COMPOSE_DRAFT_CONTENT_KEY), logicalId)) {
        s.removeItem(COMPOSE_DRAFT_CONTENT_KEY);
      }
      return;
    }
    for (const key of draftKeys(s)) s.removeItem(key);
    s.removeItem(COMPOSE_DRAFT_CONTENT_KEY);
  } catch {
    // best-effort
  }
}
