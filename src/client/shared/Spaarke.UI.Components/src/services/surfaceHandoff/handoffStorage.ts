/**
 * surfaceHandoff/handoffStorage.ts — the `sessionStorage` rendezvous (design §2).
 *
 * spaarkeai-assistant-enhancements-r1 task 012.
 *
 * The transport for the Class-2b hand-off: the Assistant host and the launched
 * surface are same-origin in the same tab (`navigateTo target:2` = in-page modal
 * iframe), so a `sessionStorage` key correlated by a minted `handoffId` carries
 * the payload out and the outcome back. This module owns the id mint, the key
 * naming, and the write/read/clear primitives. Pure + jsdom-testable — no Xrm.
 *
 * KEY NAMING (design §2 build-verify #3 — align with the existing sentinel
 * convention, do not invent a parallel scheme): the codebase already namespaces
 * session keys under `sprk` (`sprk:workspace:*`, `sprk_ai2_*`,
 * `spaarke_dailyDigestShown`). This module uses the design-note-specified
 * `sprk.handoff.<id>` / `sprk.handoff.<id>.result` — a distinct, self-cleaning
 * `sprk.handoff.` namespace that never collides with the workspace/AI-session
 * keys and is discarded on read (transient rendezvous, not a store).
 *
 * ⚠️ SAME-ORIGIN sessionStorage sharing (design §2 build-verify #1) is a
 * runtime property of the Power Apps modal-iframe host that CANNOT be verified
 * in code/jsdom. If a deployment sandboxes the wizard iframe onto a different
 * subdomain, `sessionStorage` will NOT be shared and the read-side returns null.
 * `readHandoff.ts` documents the localStorage / BFF-fetch-by-id fallback seam.
 */

import type { SurfaceHandoffEnvelope, SurfaceHandoffResult } from './types';

/** The `sessionStorage` key prefix for the hand-off namespace. */
export const HANDOFF_KEY_PREFIX = 'sprk.handoff.';

/** Default envelope TTL — 15 minutes (design §3 `ttlSeconds: 900`). */
export const DEFAULT_HANDOFF_TTL_SECONDS = 900;

/** Build the envelope storage key for a hand-off id. */
export function handoffEnvelopeKey(handoffId: string): string {
  return `${HANDOFF_KEY_PREFIX}${handoffId}`;
}

/** Build the result storage key for a hand-off id. */
export function handoffResultKey(handoffId: string): string {
  return `${HANDOFF_KEY_PREFIX}${handoffId}.result`;
}

/**
 * Mint a fresh hand-off id: `"h-<guid>"` (design §1). Uses `crypto.randomUUID`
 * when available (browser + modern jsdom), falling back to a
 * timestamp+random composite that is unique-enough for a per-launch correlation
 * (never security-sensitive).
 */
export function mintHandoffId(): string {
  try {
    const g = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto;
    if (g?.randomUUID) return `h-${g.randomUUID()}`;
  } catch {
    /* fall through to composite */
  }
  const rand = Math.random().toString(16).slice(2, 10);
  return `h-${Date.now().toString(16)}-${rand}`;
}

/** Whether `sessionStorage` is reachable in the current context. */
function getSessionStorage(): Storage | null {
  try {
    if (typeof window === 'undefined' || !window.sessionStorage) return null;
    return window.sessionStorage;
  } catch {
    // Access to storage can throw (privacy mode, sandboxed iframe).
    return null;
  }
}

/**
 * Write the envelope to `sessionStorage["sprk.handoff.<id>"]`. Returns `false`
 * when storage is unavailable (the caller flags the degraded path). Never throws.
 */
export function writeHandoffEnvelope(envelope: SurfaceHandoffEnvelope): boolean {
  const store = getSessionStorage();
  if (!store) return false;
  try {
    store.setItem(handoffEnvelopeKey(envelope.handoffId), JSON.stringify(envelope));
    return true;
  } catch {
    return false;
  }
}

/**
 * Read + parse the envelope for a hand-off id. Returns `null` when absent,
 * unparseable, or EXPIRED (`createdAt + ttlSeconds` in the past — design §3).
 */
export function readHandoffEnvelope(handoffId: string): SurfaceHandoffEnvelope | null {
  const store = getSessionStorage();
  if (!store) return null;
  let raw: string | null;
  try {
    raw = store.getItem(handoffEnvelopeKey(handoffId));
  } catch {
    return null;
  }
  if (!raw) return null;
  let parsed: SurfaceHandoffEnvelope;
  try {
    parsed = JSON.parse(raw) as SurfaceHandoffEnvelope;
  } catch {
    return null;
  }
  if (!parsed || typeof parsed.handoffId !== 'string') return null;
  // TTL guard — a stale envelope (host launched long ago, storage lingered) is
  // ignored so the surface never pre-seeds from expired context.
  if (isHandoffExpired(parsed)) return null;
  return parsed;
}

/** True when the envelope is past its TTL relative to `now`. */
export function isHandoffExpired(envelope: SurfaceHandoffEnvelope, now: number = Date.now()): boolean {
  const created = Date.parse(envelope.createdAt);
  if (Number.isNaN(created)) return false; // no valid timestamp → do not expire
  const ttlMs = Math.max(0, envelope.ttlSeconds) * 1000;
  return now > created + ttlMs;
}

/**
 * Write the outcome to `sessionStorage["sprk.handoff.<id>.result"]` (the surface
 * calls this on commit/cancel). Returns `false` when storage is unavailable.
 */
export function writeHandoffResult(handoffId: string, result: SurfaceHandoffResult): boolean {
  const store = getSessionStorage();
  if (!store) return false;
  try {
    store.setItem(handoffResultKey(handoffId), JSON.stringify(result));
    return true;
  } catch {
    return false;
  }
}

/**
 * Read + parse the outcome for a hand-off id (the Assistant reads this when the
 * `navigateTo` promise resolves). Returns `null` when absent or unparseable.
 */
export function readHandoffResult(handoffId: string): SurfaceHandoffResult | null {
  const store = getSessionStorage();
  if (!store) return null;
  let raw: string | null;
  try {
    raw = store.getItem(handoffResultKey(handoffId));
  } catch {
    return null;
  }
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as SurfaceHandoffResult;
    if (!parsed || typeof parsed.committed !== 'boolean') return null;
    return parsed;
  } catch {
    return null;
  }
}

/**
 * Remove BOTH the envelope and the result keys for a hand-off id (design §1
 * step 10 — cleanup after the outcome is read). Idempotent; never throws.
 */
export function clearHandoff(handoffId: string): void {
  const store = getSessionStorage();
  if (!store) return;
  try {
    store.removeItem(handoffEnvelopeKey(handoffId));
    store.removeItem(handoffResultKey(handoffId));
  } catch {
    /* best-effort cleanup */
  }
}
