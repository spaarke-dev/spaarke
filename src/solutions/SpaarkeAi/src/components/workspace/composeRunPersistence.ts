/**
 * composeRunPersistence.ts — durable client-side persistence for the UNBOUND
 * "home-surface" Compose tab(s) and their in-flight review runs
 * (ai-advanced-capabilities-agreements-r1, UAT round-5 item #13).
 *
 * ── Why this exists (the exact gap it closes) ────────────────────────────────
 * WorkspacePane already persists+restores workspace tabs two ways (task 065 /
 * 025): a BFF `/tabs` store keyed on `chatSessionId`, and a localStorage anchor
 * (`tabAnchorKeyForContext`, `sprk_ai2_workspaceTabs__<entity>:<id>`) keyed on
 * the host `entityContext` (the bound Analysis/Matter record). BOTH miss the
 * owner's repro exactly:
 *   - The plain SpaarkeAi "home" surface (Daily Briefing → direct-Compose door)
 *     has NO `entityContext` → `tabAnchorKeyForContext` returns null → the local
 *     anchor never writes.
 *   - A direct-Compose session is UNBOUND → `chatSessionId` is null at tab-open
 *     time (it is lazily minted later by the DEF-10 register), so the BFF `/tabs`
 *     PATCH is skipped; and on cold reload the entityless base `chatSessionId`
 *     key does not restore it either. Net: the Compose tab is genuinely lost on
 *     return (confirmed cold-load analysis, 2026-08-04).
 *
 * ── Design (smallest honest version; §11 extend, don't invent) ───────────────
 * This layer persists ONLY the home-surface Compose tab(s) — the one surface the
 * two shipped mechanisms decline to cover — keyed on the SAME per-document
 * identity WorkspacePane already uses (`deriveComposeInstanceKey`), to a single
 * localStorage entry. It follows the EXISTING restore convention (localStorage,
 * the `sprk_ai2_*` key family) rather than sessionStorage, per the owner's own
 * conditional ("if the existing restore infra uses localStorage, follow ITS
 * convention") — and because localStorage reliably survives the code-page iframe
 * teardown/recreate that same-tab MDA navigation performs, where sessionStorage's
 * nested-browsing-context lifetime is ambiguous. Freshness + agency are bounded
 * by a TTL (hours, not days) + explicit-close removal; the schema is versioned +
 * additive.
 *
 * The module is PURE (no React, no direct global reads except through the
 * injectable `Storage` param) so every branch is unit-testable in isolation.
 * WorkspacePane owns the `deriveComposeInstanceKey` derivation and the PaneEvent
 * wiring; this module owns only the storage shape + freshness/resumability rules.
 *
 * @see WorkspacePane.tsx — tabAnchorKeyForContext / deriveComposeInstanceKey /
 *      the persist-on-open, clear-on-close, cold-load restore + resume poll wiring
 * @see ChatEndpoints.ProjectComposeOutputs — GET /api/ai/chat/sessions/{id}/compose-outputs
 *      (the ledger the resume poll reads for findings)
 */

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * localStorage key for the home-surface Compose-tab snapshot. Namespaced under
 * the SAME `sprk_ai2_*` family as `AiSessionProvider.chatSessionKeyForContext`
 * (`sprk_ai2_chatSessionId`) and `tabAnchorKeyForContext`
 * (`sprk_ai2_workspaceTabs`). A single entityless key — the home surface has no
 * record to anchor on (that is precisely why the shipped anchors miss it).
 */
export const COMPOSE_RUN_STATE_STORAGE_KEY = 'sprk_ai2_composeRunState';

/** Schema version — bump only on a breaking shape change; unknown versions are ignored (treated as absent). */
export const COMPOSE_RUN_STATE_SCHEMA_VERSION = 1;

/**
 * Freshness guard. A persisted Compose tab older than this is dropped on read —
 * long enough to survive a normal work session's navigation (lunch, meetings),
 * short enough that yesterday's tab does not silently resurrect. 8 hours.
 */
export const COMPOSE_RUN_STATE_TTL_MS = 8 * 60 * 60 * 1000;

/**
 * A review run is only "resumable" (spinner + poll) if it was dispatched within
 * this window. The window must cover the WHOLE span from the client
 * `dispatchedAt` stamp until the review's findings are durably readable in the
 * compose-outputs ledger, in the worst case:
 *   - retrieval (1 embedding + 1 hybrid search, additive BEFORE the LLM): ~2–5s
 *   - the BFF's per-attempt OpenAI network timeout for the slow gpt-5 Reasoning
 *     review: 300s (the review call itself is measured at ~120–180s but can run
 *     to the 300s ceiling on a hard case)
 *   - ledger write + SSE tail: ~1–3s
 *   - client dispatch→server-start latency + the resume poll's own 5s
 *     granularity + client/server clock skew between the `dispatchedAt` stamp
 *     and the server LLM window: several more seconds
 * That sums to ~310–320s of genuinely-live time. The prior 330s left only ~15s
 * of margin over that ceiling — too thin once retrieval is counted as ADDITIVE
 * to the 300s timeout (retrieval runs, THEN the up-to-300s completion). 420s
 * (UAT round-6 item #15a) gives a comfortable margin that absorbs a slow
 * retrieval + clock skew + realistic return-navigation latency while staying far
 * under the 8h TTL and the explicit-close agency bound. A ~2–3min Thorough run
 * (the owner's repro) sits comfortably inside it. Past this window a run that
 * produced nothing is assumed dead — we clear the flag and show only the
 * document (findings, if any, still materialize on a later open via FR-16
 * durable recall on the reopened tab's mount-materialize).
 */
export const COMPOSE_RUN_IN_FLIGHT_MAX_MS = 420_000;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** In-flight review-run metadata carried alongside a persisted Compose tab. */
export interface PersistedComposeRun {
  /** True while a review dispatched from this tab is believed to still be executing server-side. */
  inFlight: boolean;
  /** Epoch ms when the run started (the young-run guard reads this — see COMPOSE_RUN_IN_FLIGHT_MAX_MS). */
  dispatchedAt: number;
}

/** One persisted home-surface Compose tab — everything needed to remount + resume it. */
export interface PersistedComposeTab {
  /** Per-document identity (WorkspacePane.deriveComposeInstanceKey) — the upsert/remove/dedupe key. */
  instanceKey: string;
  /** Always 'compose' — the Direct widget type this remounts as. */
  widgetType: 'compose';
  /** The tab's remount-essential `widgetData` (the `{ compose: {...} }` seed + hoisted `filename`). */
  widgetData: unknown;
  /** Human-readable tab label. */
  displayName: string;
  /** Epoch ms of the last write — the TTL freshness anchor. */
  savedAt: number;
  /**
   * The chat≡document session id to (a) resume the document on reopen
   * (`composeSessionId` → `initialSessionId` → BFF Load `?sessionId=`) so FR-16
   * durable recall re-materializes completed findings, and (b) poll
   * compose-outputs on for the still-running case. Absent when no session was
   * minted yet (no review ran) — the tab still restores the document.
   */
  sessionId?: string;
  /** In-flight review metadata; absent when no review was dispatched from this tab. */
  run?: PersistedComposeRun;
}

/** The versioned localStorage envelope. */
export interface ComposeRunPersistenceSnapshot {
  version: number;
  tabs: PersistedComposeTab[];
}

/** Minimal Storage surface (localStorage in production; an in-memory double in tests). */
export interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

/** The client projection of one `GET .../compose-outputs` ledger entry (camelCased by the BFF). */
export interface ComposeLedgerOutputLike {
  key?: string;
  bindingId?: string;
  turn?: number;
  disposition?: string;
  payload?: unknown;
}

// ---------------------------------------------------------------------------
// Storage access (best-effort — mirrors WorkspacePane.readLocalTabSnapshot)
// ---------------------------------------------------------------------------

function resolveStorage(storage?: StorageLike): StorageLike | null {
  if (storage) return storage;
  try {
    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
    if (typeof localStorage !== 'undefined' && localStorage) return localStorage;
  } catch {
    /* localStorage may throw in private mode / sandboxed contexts. */
  }
  return null;
}

/**
 * Read + validate + TTL-prune the persisted snapshot. Returns a snapshot whose
 * `tabs` are all FRESH (savedAt within TTL), or null when absent/unparseable/
 * wrong-version/empty. Never throws.
 */
export function readComposeRunState(
  now: number,
  storage?: StorageLike,
  ttlMs: number = COMPOSE_RUN_STATE_TTL_MS
): ComposeRunPersistenceSnapshot | null {
  const store = resolveStorage(storage);
  if (!store) return null;
  let raw: string | null = null;
  try {
    raw = store.getItem(COMPOSE_RUN_STATE_STORAGE_KEY);
  } catch {
    return null;
  }
  if (!raw) return null;
  let parsed: ComposeRunPersistenceSnapshot;
  try {
    parsed = JSON.parse(raw) as ComposeRunPersistenceSnapshot;
  } catch {
    return null;
  }
  if (!parsed || parsed.version !== COMPOSE_RUN_STATE_SCHEMA_VERSION || !Array.isArray(parsed.tabs)) {
    return null;
  }
  const fresh = pruneExpiredTabs(parsed.tabs, now, ttlMs);
  if (fresh.length === 0) return null;
  return { version: COMPOSE_RUN_STATE_SCHEMA_VERSION, tabs: fresh };
}

/** Persist a snapshot (best-effort — swallows quota/security errors). Empty snapshots remove the key. */
export function writeComposeRunState(
  snapshot: ComposeRunPersistenceSnapshot | null,
  storage?: StorageLike
): void {
  const store = resolveStorage(storage);
  if (!store) return;
  try {
    if (!snapshot || snapshot.tabs.length === 0) {
      store.removeItem(COMPOSE_RUN_STATE_STORAGE_KEY);
      return;
    }
    store.setItem(COMPOSE_RUN_STATE_STORAGE_KEY, JSON.stringify(snapshot));
  } catch {
    /* best-effort only */
  }
}

// ---------------------------------------------------------------------------
// Pure snapshot transforms
// ---------------------------------------------------------------------------

/** Drop tabs whose `savedAt` is older than `now - ttlMs` (also drops malformed savedAt). */
export function pruneExpiredTabs(
  tabs: readonly PersistedComposeTab[],
  now: number,
  ttlMs: number = COMPOSE_RUN_STATE_TTL_MS
): PersistedComposeTab[] {
  return tabs.filter(
    t => typeof t.savedAt === 'number' && Number.isFinite(t.savedAt) && now - t.savedAt <= ttlMs
  );
}

/**
 * Upsert one Compose tab by `instanceKey`. Merges over any prior entry:
 * `widgetData`/`displayName`/`savedAt` always take the new value; `sessionId`
 * and `run` are PRESERVED from the prior entry when the incoming value is
 * undefined (so a plain persist-on-open never wipes a session/run captured
 * later). Returns a NEW snapshot (never mutates the input).
 */
export function upsertPersistedComposeTab(
  prev: ComposeRunPersistenceSnapshot | null,
  next: PersistedComposeTab
): ComposeRunPersistenceSnapshot {
  const prevTabs = prev?.tabs ?? [];
  const existing = prevTabs.find(t => t.instanceKey === next.instanceKey);
  const merged: PersistedComposeTab = {
    ...next,
    sessionId: next.sessionId ?? existing?.sessionId,
    run: next.run ?? existing?.run,
  };
  const tabs = existing
    ? prevTabs.map(t => (t.instanceKey === next.instanceKey ? merged : t))
    : [...prevTabs, merged];
  return { version: COMPOSE_RUN_STATE_SCHEMA_VERSION, tabs };
}

/** Remove one Compose tab by `instanceKey` (explicit-close agency). Returns a new snapshot (or null when empty). */
export function removePersistedComposeTab(
  prev: ComposeRunPersistenceSnapshot | null,
  instanceKey: string
): ComposeRunPersistenceSnapshot | null {
  const prevTabs = prev?.tabs ?? [];
  const tabs = prevTabs.filter(t => t.instanceKey !== instanceKey);
  if (tabs.length === 0) return null;
  return { version: COMPOSE_RUN_STATE_SCHEMA_VERSION, tabs };
}

/** Mark a tab's review run in-flight (persist-on-run-start). No-op when the tab is absent. */
export function markRunInFlight(
  prev: ComposeRunPersistenceSnapshot | null,
  instanceKey: string,
  run: PersistedComposeRun,
  sessionId?: string
): ComposeRunPersistenceSnapshot | null {
  const prevTabs = prev?.tabs ?? [];
  if (!prevTabs.some(t => t.instanceKey === instanceKey)) return prev ?? null;
  const tabs = prevTabs.map(t =>
    t.instanceKey === instanceKey
      ? { ...t, run, sessionId: sessionId ?? t.sessionId, savedAt: run.dispatchedAt }
      : t
  );
  return { version: COMPOSE_RUN_STATE_SCHEMA_VERSION, tabs };
}

/** Clear a tab's in-flight flag (run reached a terminal state / TTL). Keeps the tab + its session. */
export function clearRunInFlight(
  prev: ComposeRunPersistenceSnapshot | null,
  instanceKey: string
): ComposeRunPersistenceSnapshot | null {
  const prevTabs = prev?.tabs ?? [];
  if (!prevTabs.some(t => t.instanceKey === instanceKey && t.run?.inFlight)) return prev ?? null;
  const tabs = prevTabs.map(t =>
    t.instanceKey === instanceKey && t.run
      ? { ...t, run: { ...t.run, inFlight: false } }
      : t
  );
  return { version: COMPOSE_RUN_STATE_SCHEMA_VERSION, tabs };
}

/**
 * Clear the in-flight flag for EVERY tab whose captured `sessionId` matches
 * (UAT round-6 item #15a — clear-on-completion). The dispatch-time stamp keys a
 * run to the ACTIVE Compose tab, but a review can COMPLETE while the user has
 * switched to a different workspace tab (the "Continue working in background"
 * case), so completion clearing keys on the SESSION the completion event carries
 * (`compose_advisory_comments.sessionId`) rather than the currently-active tab.
 * No-op (returns the input unchanged) when `sessionId` is falsy or no in-flight
 * tab matches. Never mutates the input.
 */
export function clearRunInFlightBySession(
  prev: ComposeRunPersistenceSnapshot | null,
  sessionId: string | undefined
): ComposeRunPersistenceSnapshot | null {
  const prevTabs = prev?.tabs ?? [];
  if (!sessionId) return prev ?? null;
  if (!prevTabs.some(t => t.sessionId === sessionId && t.run?.inFlight)) return prev ?? null;
  const tabs = prevTabs.map(t =>
    t.sessionId === sessionId && t.run ? { ...t, run: { ...t.run, inFlight: false } } : t
  );
  return { version: COMPOSE_RUN_STATE_SCHEMA_VERSION, tabs };
}

// ---------------------------------------------------------------------------
// Resumability + findings detection
// ---------------------------------------------------------------------------

/** True when a persisted run is in-flight AND young enough to still be executing (spinner + poll window). */
export function isRunResumable(
  run: PersistedComposeRun | undefined,
  now: number,
  maxMs: number = COMPOSE_RUN_IN_FLIGHT_MAX_MS
): boolean {
  if (!run || !run.inFlight) return false;
  if (typeof run.dispatchedAt !== 'number' || !Number.isFinite(run.dispatchedAt)) return false;
  return now - run.dispatchedAt < maxMs;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

/**
 * From a `GET .../compose-outputs` array, return the LATEST review-findings
 * payload (`{ overallRisk, flaggedSections[] }`) or null. "Findings-shaped" =
 * a `flaggedSections` array (the SAME structural discriminant
 * `isNdaReviewResult` + `isFindingsShapedComposeOutput` use). Picks the highest
 * `turn` so a Thorough rerun supersedes an earlier Quick scan. A findings entry
 * with an EMPTY `flaggedSections` array is a valid "clean review" completion and
 * is returned (its `flaggedSections.length === 0`); callers decide what to do.
 */
export function findLatestFindingsPayload(
  composeOutputs: readonly ComposeLedgerOutputLike[] | null | undefined
): { overallRisk: string; flaggedSections: unknown[] } | null {
  if (!Array.isArray(composeOutputs)) return null;
  let best: { overallRisk: string; flaggedSections: unknown[]; turn: number } | null = null;
  for (const o of composeOutputs) {
    if (o?.disposition !== 'compose') continue;
    const payload = asRecord(o.payload);
    if (!payload) continue;
    if (!Array.isArray(payload.flaggedSections)) continue;
    if (typeof payload.overallRisk !== 'string') continue;
    const turn = typeof o.turn === 'number' ? o.turn : 0;
    if (!best || turn >= best.turn) {
      best = { overallRisk: payload.overallRisk, flaggedSections: payload.flaggedSections as unknown[], turn };
    }
  }
  if (!best) return null;
  return { overallRisk: best.overallRisk, flaggedSections: best.flaggedSections };
}

/** True when the compose-outputs ledger already carries a completed review (findings-shaped payload present). */
export function hasFindings(
  composeOutputs: readonly ComposeLedgerOutputLike[] | null | undefined
): boolean {
  return findLatestFindingsPayload(composeOutputs) !== null;
}

/**
 * Thread a resumed session id into a persisted Compose seed as
 * `compose.composeSessionId` so `ComposeDirectWidget` forwards it as
 * `initialSessionId` (→ BFF Load `?sessionId=` resume → FR-16 recall). Returns a
 * shallow clone; leaves the seed untouched when no session id or no `compose`
 * object is present. Never mutates the input.
 */
export function withComposeSessionId(widgetData: unknown, sessionId: string | undefined): unknown {
  if (!sessionId) return widgetData;
  const wd = asRecord(widgetData);
  if (!wd) return widgetData;
  const compose = asRecord(wd.compose);
  if (!compose) return widgetData;
  return { ...wd, compose: { ...compose, composeSessionId: sessionId } };
}
