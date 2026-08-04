/**
 * composeRunPersistence — pure-module unit tests (UAT round-5 item #13).
 *
 * Covers the storage-shape + freshness/resumability rules that back the
 * home-surface Compose-tab persistence + resume: read/validate/prune, upsert
 * (merge semantics), remove (explicit-close agency), run-in-flight mark/clear,
 * TTL + young-run guards, findings detection, and the composeSessionId seed
 * injection.
 */

import {
  COMPOSE_RUN_STATE_STORAGE_KEY,
  COMPOSE_RUN_STATE_SCHEMA_VERSION,
  COMPOSE_RUN_STATE_TTL_MS,
  COMPOSE_RUN_IN_FLIGHT_MAX_MS,
  readComposeRunState,
  writeComposeRunState,
  pruneExpiredTabs,
  upsertPersistedComposeTab,
  removePersistedComposeTab,
  markRunInFlight,
  clearRunInFlight,
  clearRunInFlightBySession,
  isRunResumable,
  findLatestFindingsPayload,
  hasFindings,
  withComposeSessionId,
  type StorageLike,
  type PersistedComposeTab,
  type ComposeRunPersistenceSnapshot,
  type ComposeLedgerOutputLike,
} from '../composeRunPersistence';

// ---------------------------------------------------------------------------
// In-memory Storage double
// ---------------------------------------------------------------------------

function makeStorage(seed?: Record<string, string>): StorageLike & { map: Map<string, string> } {
  const map = new Map<string, string>(Object.entries(seed ?? {}));
  return {
    map,
    getItem: (k: string) => (map.has(k) ? (map.get(k) as string) : null),
    setItem: (k: string, v: string) => {
      map.set(k, v);
    },
    removeItem: (k: string) => {
      map.delete(k);
    },
  };
}

function tab(overrides: Partial<PersistedComposeTab> = {}): PersistedComposeTab {
  return {
    instanceKey: 'upload:file-1',
    widgetType: 'compose',
    widgetData: { compose: { upload: { sessionId: 's1', sessionFileId: 'file-1' }, fileName: 'NDA.docx' } },
    displayName: 'NDA.docx',
    savedAt: 1_000,
    ...overrides,
  };
}

const NOW = 10_000_000;

// ---------------------------------------------------------------------------
// read / write round-trip + validation
// ---------------------------------------------------------------------------

describe('readComposeRunState / writeComposeRunState', () => {
  it('round-trips a fresh snapshot', () => {
    const store = makeStorage();
    const snap: ComposeRunPersistenceSnapshot = {
      version: COMPOSE_RUN_STATE_SCHEMA_VERSION,
      tabs: [tab({ savedAt: NOW })],
    };
    writeComposeRunState(snap, store);
    expect(store.map.has(COMPOSE_RUN_STATE_STORAGE_KEY)).toBe(true);
    const read = readComposeRunState(NOW, store);
    expect(read).not.toBeNull();
    expect(read?.tabs).toHaveLength(1);
    expect(read?.tabs[0].instanceKey).toBe('upload:file-1');
  });

  it('returns null when nothing is stored', () => {
    expect(readComposeRunState(NOW, makeStorage())).toBeNull();
  });

  it('returns null for unparseable JSON', () => {
    const store = makeStorage({ [COMPOSE_RUN_STATE_STORAGE_KEY]: '{not json' });
    expect(readComposeRunState(NOW, store)).toBeNull();
  });

  it('ignores an unknown schema version (forward/backward incompat)', () => {
    const store = makeStorage({
      [COMPOSE_RUN_STATE_STORAGE_KEY]: JSON.stringify({ version: 999, tabs: [tab({ savedAt: NOW })] }),
    });
    expect(readComposeRunState(NOW, store)).toBeNull();
  });

  it('writing an empty/null snapshot REMOVES the key', () => {
    const store = makeStorage({ [COMPOSE_RUN_STATE_STORAGE_KEY]: JSON.stringify({ version: 1, tabs: [tab()] }) });
    writeComposeRunState(null, store);
    expect(store.map.has(COMPOSE_RUN_STATE_STORAGE_KEY)).toBe(false);
    writeComposeRunState({ version: COMPOSE_RUN_STATE_SCHEMA_VERSION, tabs: [] }, store);
    expect(store.map.has(COMPOSE_RUN_STATE_STORAGE_KEY)).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// TTL pruning
// ---------------------------------------------------------------------------

describe('pruneExpiredTabs / TTL on read', () => {
  it('drops tabs older than the TTL and keeps fresh ones', () => {
    const fresh = tab({ instanceKey: 'upload:fresh', savedAt: NOW - 1000 });
    const stale = tab({ instanceKey: 'upload:stale', savedAt: NOW - COMPOSE_RUN_STATE_TTL_MS - 1 });
    const kept = pruneExpiredTabs([fresh, stale], NOW);
    expect(kept.map(t => t.instanceKey)).toEqual(['upload:fresh']);
  });

  it('readComposeRunState returns null when EVERY tab is expired', () => {
    const store = makeStorage();
    writeComposeRunState(
      { version: COMPOSE_RUN_STATE_SCHEMA_VERSION, tabs: [tab({ savedAt: NOW - COMPOSE_RUN_STATE_TTL_MS - 5 })] },
      store
    );
    expect(readComposeRunState(NOW, store)).toBeNull();
  });

  it('a tab exactly at the TTL boundary is kept (<=)', () => {
    const boundary = tab({ savedAt: NOW - COMPOSE_RUN_STATE_TTL_MS });
    expect(pruneExpiredTabs([boundary], NOW)).toHaveLength(1);
  });
});

// ---------------------------------------------------------------------------
// upsert merge semantics
// ---------------------------------------------------------------------------

describe('upsertPersistedComposeTab', () => {
  it('adds a new tab when the key is absent', () => {
    const out = upsertPersistedComposeTab(null, tab());
    expect(out.tabs).toHaveLength(1);
    expect(out.version).toBe(COMPOSE_RUN_STATE_SCHEMA_VERSION);
  });

  it('replaces widgetData/displayName/savedAt but PRESERVES prior run + sessionId when new is undefined', () => {
    const prior: ComposeRunPersistenceSnapshot = {
      version: COMPOSE_RUN_STATE_SCHEMA_VERSION,
      tabs: [tab({ sessionId: 'sess-keep', run: { inFlight: true, dispatchedAt: 42 }, savedAt: 1 })],
    };
    const out = upsertPersistedComposeTab(prior, {
      instanceKey: 'upload:file-1',
      widgetType: 'compose',
      widgetData: { compose: { upload: { sessionFileId: 'file-1' } }, filename: 'renamed.docx' },
      displayName: 'renamed.docx',
      savedAt: 500,
      // sessionId + run intentionally omitted (a plain persist-on-open)
    });
    expect(out.tabs).toHaveLength(1);
    expect(out.tabs[0].displayName).toBe('renamed.docx');
    expect(out.tabs[0].savedAt).toBe(500);
    expect(out.tabs[0].sessionId).toBe('sess-keep'); // preserved
    expect(out.tabs[0].run).toEqual({ inFlight: true, dispatchedAt: 42 }); // preserved
  });

  it('a new run/sessionId OVERRIDES the prior', () => {
    const prior: ComposeRunPersistenceSnapshot = {
      version: COMPOSE_RUN_STATE_SCHEMA_VERSION,
      tabs: [tab({ sessionId: 'old', run: { inFlight: false, dispatchedAt: 1 } })],
    };
    const out = upsertPersistedComposeTab(prior, tab({ sessionId: 'new', run: { inFlight: true, dispatchedAt: 99 } }));
    expect(out.tabs[0].sessionId).toBe('new');
    expect(out.tabs[0].run).toEqual({ inFlight: true, dispatchedAt: 99 });
  });

  it('does not mutate the input snapshot', () => {
    const prior: ComposeRunPersistenceSnapshot = { version: 1, tabs: [tab()] };
    const snapshotBefore = JSON.stringify(prior);
    upsertPersistedComposeTab(prior, tab({ instanceKey: 'upload:file-2' }));
    expect(JSON.stringify(prior)).toBe(snapshotBefore);
  });

  it('supports multiple distinct Compose tabs additively', () => {
    let snap = upsertPersistedComposeTab(null, tab({ instanceKey: 'upload:a' }));
    snap = upsertPersistedComposeTab(snap, tab({ instanceKey: 'stored:b' }));
    expect(snap.tabs.map(t => t.instanceKey).sort()).toEqual(['stored:b', 'upload:a']);
  });
});

// ---------------------------------------------------------------------------
// remove (explicit-close agency)
// ---------------------------------------------------------------------------

describe('removePersistedComposeTab', () => {
  it('removes the matching key and returns null when the set becomes empty', () => {
    const prior: ComposeRunPersistenceSnapshot = { version: 1, tabs: [tab({ instanceKey: 'upload:only' })] };
    expect(removePersistedComposeTab(prior, 'upload:only')).toBeNull();
  });

  it('keeps other tabs', () => {
    const prior: ComposeRunPersistenceSnapshot = {
      version: 1,
      tabs: [tab({ instanceKey: 'upload:a' }), tab({ instanceKey: 'upload:b' })],
    };
    const out = removePersistedComposeTab(prior, 'upload:a');
    expect(out?.tabs.map(t => t.instanceKey)).toEqual(['upload:b']);
  });

  it('is a no-op-shaped null when prev is null', () => {
    expect(removePersistedComposeTab(null, 'x')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// run mark / clear + resumability
// ---------------------------------------------------------------------------

describe('markRunInFlight / clearRunInFlight / isRunResumable', () => {
  it('marks an existing tab in-flight with the session', () => {
    const prior: ComposeRunPersistenceSnapshot = { version: 1, tabs: [tab({ instanceKey: 'upload:a' })] };
    const out = markRunInFlight(prior, 'upload:a', { inFlight: true, dispatchedAt: NOW }, 'sess-1');
    expect(out?.tabs[0].run).toEqual({ inFlight: true, dispatchedAt: NOW });
    expect(out?.tabs[0].sessionId).toBe('sess-1');
    expect(out?.tabs[0].savedAt).toBe(NOW);
  });

  it('mark is a no-op for an absent tab', () => {
    const prior: ComposeRunPersistenceSnapshot = { version: 1, tabs: [tab({ instanceKey: 'upload:a' })] };
    const out = markRunInFlight(prior, 'upload:missing', { inFlight: true, dispatchedAt: NOW });
    expect(out).toBe(prior);
  });

  it('clear flips inFlight false but keeps the tab + session', () => {
    const prior: ComposeRunPersistenceSnapshot = {
      version: 1,
      tabs: [tab({ instanceKey: 'upload:a', sessionId: 'sess-1', run: { inFlight: true, dispatchedAt: NOW } })],
    };
    const out = clearRunInFlight(prior, 'upload:a');
    expect(out?.tabs[0].run).toEqual({ inFlight: false, dispatchedAt: NOW });
    expect(out?.tabs[0].sessionId).toBe('sess-1');
  });

  it('isRunResumable: true only for an in-flight, young run', () => {
    expect(isRunResumable({ inFlight: true, dispatchedAt: NOW - 1000 }, NOW)).toBe(true);
    expect(isRunResumable({ inFlight: false, dispatchedAt: NOW - 1000 }, NOW)).toBe(false);
    expect(isRunResumable({ inFlight: true, dispatchedAt: NOW - COMPOSE_RUN_IN_FLIGHT_MAX_MS - 1 }, NOW)).toBe(false);
    expect(isRunResumable(undefined, NOW)).toBe(false);
  });

  // UAT round-6 (item #15a): the resumability window must cover a worst-case dispatch→durable-findings
  // span — retrieval (additive) + the 300s OpenAI per-attempt timeout + ledger/SSE tail + client poll
  // granularity + clock skew. 420s comfortably covers a ~2–3min Thorough run AND a near-timeout run.
  it('isRunResumable: the 420s window covers a ~3min run + a near-timeout run, but not a stale one', () => {
    expect(COMPOSE_RUN_IN_FLIGHT_MAX_MS).toBe(420_000);
    // A ~3min Thorough run (the owner's repro) that started 3min ago is still resumable.
    expect(isRunResumable({ inFlight: true, dispatchedAt: NOW - 180_000 }, NOW)).toBe(true);
    // A run that hit the ~300s ceiling and the user returns shortly after is still resumable.
    expect(isRunResumable({ inFlight: true, dispatchedAt: NOW - 310_000 }, NOW)).toBe(true);
    // Past the window, a run that produced nothing is treated as dead.
    expect(isRunResumable({ inFlight: true, dispatchedAt: NOW - 420_001 }, NOW)).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// clearRunInFlightBySession (UAT round-6 item #15a — completion clear keyed by session)
// ---------------------------------------------------------------------------

describe('clearRunInFlightBySession', () => {
  it('clears the in-flight flag for the tab bound to the matching session', () => {
    const prior: ComposeRunPersistenceSnapshot = {
      version: 1,
      tabs: [
        tab({ instanceKey: 'upload:a', sessionId: 'sess-1', run: { inFlight: true, dispatchedAt: NOW } }),
        tab({ instanceKey: 'upload:b', sessionId: 'sess-2', run: { inFlight: true, dispatchedAt: NOW } }),
      ],
    };
    const out = clearRunInFlightBySession(prior, 'sess-1');
    expect(out?.tabs.find(t => t.instanceKey === 'upload:a')?.run?.inFlight).toBe(false);
    // The other session's tab is untouched.
    expect(out?.tabs.find(t => t.instanceKey === 'upload:b')?.run?.inFlight).toBe(true);
    // The tab + its session are preserved (only the flag flips).
    expect(out?.tabs.find(t => t.instanceKey === 'upload:a')?.sessionId).toBe('sess-1');
  });

  it('is a no-op when no in-flight tab matches the session (and when sessionId is falsy)', () => {
    const prior: ComposeRunPersistenceSnapshot = {
      version: 1,
      tabs: [tab({ instanceKey: 'upload:a', sessionId: 'sess-1', run: { inFlight: true, dispatchedAt: NOW } })],
    };
    expect(clearRunInFlightBySession(prior, 'sess-none')).toBe(prior);
    expect(clearRunInFlightBySession(prior, undefined)).toBe(prior);
    expect(clearRunInFlightBySession(null, 'sess-1')).toBeNull();
  });

  it('is a no-op when the matching tab is already not in-flight (idempotent)', () => {
    const prior: ComposeRunPersistenceSnapshot = {
      version: 1,
      tabs: [tab({ instanceKey: 'upload:a', sessionId: 'sess-1', run: { inFlight: false, dispatchedAt: NOW } })],
    };
    expect(clearRunInFlightBySession(prior, 'sess-1')).toBe(prior);
  });
});

// ---------------------------------------------------------------------------
// findings detection (the resume poll's signal)
// ---------------------------------------------------------------------------

describe('findLatestFindingsPayload / hasFindings', () => {
  const findingEntry = (turn: number, risk: string, n: number): ComposeLedgerOutputLike => ({
    key: `b1@t${turn}`,
    bindingId: 'b1',
    turn,
    disposition: 'compose',
    payload: {
      overallRisk: risk,
      flaggedSections: Array.from({ length: n }, (_, i) => ({ quotedText: `q${i}`, explanation: `e${i}` })),
    },
  });

  it('returns null for empty / non-findings ledgers', () => {
    expect(findLatestFindingsPayload(null)).toBeNull();
    expect(findLatestFindingsPayload([])).toBeNull();
    expect(hasFindings([])).toBe(false);
    expect(
      hasFindings([{ disposition: 'compose', turn: 1, payload: { html: '<p>draft</p>' } }])
    ).toBe(false);
  });

  it('detects a findings-shaped compose output', () => {
    expect(hasFindings([findingEntry(1, 'medium', 2)])).toBe(true);
    const payload = findLatestFindingsPayload([findingEntry(1, 'medium', 2)]);
    expect(payload?.overallRisk).toBe('medium');
    expect(payload?.flaggedSections).toHaveLength(2);
  });

  it('picks the LATEST turn (Thorough supersedes Quick)', () => {
    const payload = findLatestFindingsPayload([findingEntry(1, 'low', 1), findingEntry(2, 'high', 3)]);
    expect(payload?.overallRisk).toBe('high');
    expect(payload?.flaggedSections).toHaveLength(3);
  });

  it('treats a clean (zero-flag) review as present findings (length 0)', () => {
    const clean = findingEntry(1, 'low', 0);
    expect(hasFindings([clean])).toBe(true);
    expect(findLatestFindingsPayload([clean])?.flaggedSections).toHaveLength(0);
  });

  it('ignores non-compose dispositions', () => {
    expect(
      hasFindings([{ disposition: 'chat', turn: 1, payload: { overallRisk: 'x', flaggedSections: [{}] } }])
    ).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// seed injection
// ---------------------------------------------------------------------------

describe('withComposeSessionId', () => {
  it('threads the session id into the compose seed', () => {
    const out = withComposeSessionId({ compose: { speDriveItemId: 'd1' } }, 'sess-9') as {
      compose: { composeSessionId?: string; speDriveItemId?: string };
    };
    expect(out.compose.composeSessionId).toBe('sess-9');
    expect(out.compose.speDriveItemId).toBe('d1');
  });

  it('returns the input unchanged when no session id', () => {
    const wd = { compose: { speDriveItemId: 'd1' } };
    expect(withComposeSessionId(wd, undefined)).toBe(wd);
  });

  it('returns the input unchanged when there is no compose object', () => {
    const wd = { notCompose: true };
    expect(withComposeSessionId(wd, 'sess-9')).toBe(wd);
  });

  it('does not mutate the input', () => {
    const wd = { compose: { speDriveItemId: 'd1' } };
    withComposeSessionId(wd, 'sess-9');
    expect((wd.compose as { composeSessionId?: string }).composeSessionId).toBeUndefined();
  });
});
