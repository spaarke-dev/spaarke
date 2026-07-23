/**
 * handoffStorage.test.ts — the sessionStorage rendezvous (task 012, design §2).
 * Covers: id mint format, key naming, envelope round-trip, TTL expiry,
 * result round-trip, and cleanup (both keys).
 */
import {
  HANDOFF_KEY_PREFIX,
  DEFAULT_HANDOFF_TTL_SECONDS,
  handoffEnvelopeKey,
  handoffResultKey,
  mintHandoffId,
  writeHandoffEnvelope,
  readHandoffEnvelope,
  isHandoffExpired,
  writeHandoffResult,
  readHandoffResult,
  clearHandoff,
} from '../surfaceHandoff/handoffStorage';
import type { SurfaceHandoffEnvelope } from '../surfaceHandoff/types';

function makeEnvelope(overrides: Partial<SurfaceHandoffEnvelope> = {}): SurfaceHandoffEnvelope {
  return {
    handoffId: 'h-test-1',
    source: { sessionId: 's1', bindingId: 'b1', turn: 3, consumerType: 'create-matter' },
    target: { surface: 'sprk_creatematterwizard', kind: 'wizard' },
    fileIds: ['spe-file-1', 'spe-file-2'],
    draftValues: { sprk_mattername: 'Acme NDA intake' },
    resolvedLookups: {},
    provenance: { sourceFiles: ['nda-acme.pdf'] },
    createdAt: new Date().toISOString(),
    ttlSeconds: DEFAULT_HANDOFF_TTL_SECONDS,
    ...overrides,
  };
}

describe('handoffStorage', () => {
  beforeEach(() => sessionStorage.clear());

  it('mints ids of the form "h-<...>" and unique per call', () => {
    const a = mintHandoffId();
    const b = mintHandoffId();
    expect(a).toMatch(/^h-/);
    expect(b).toMatch(/^h-/);
    expect(a).not.toBe(b);
  });

  it('keys use the sprk.handoff. namespace + .result suffix', () => {
    expect(handoffEnvelopeKey('h-9')).toBe('sprk.handoff.h-9');
    expect(handoffResultKey('h-9')).toBe('sprk.handoff.h-9.result');
    expect(HANDOFF_KEY_PREFIX).toBe('sprk.handoff.');
  });

  it('round-trips an envelope through sessionStorage (files by reference)', () => {
    const env = makeEnvelope();
    expect(writeHandoffEnvelope(env)).toBe(true);
    // Stored under the namespaced key.
    expect(sessionStorage.getItem('sprk.handoff.h-test-1')).toBeTruthy();
    const read = readHandoffEnvelope('h-test-1');
    expect(read).not.toBeNull();
    expect(read!.fileIds).toEqual(['spe-file-1', 'spe-file-2']);
    expect(read!.draftValues).toEqual({ sprk_mattername: 'Acme NDA intake' });
    expect(read!.target).toEqual({ surface: 'sprk_creatematterwizard', kind: 'wizard' });
  });

  it('returns null for a missing envelope', () => {
    expect(readHandoffEnvelope('h-none')).toBeNull();
  });

  it('ignores an EXPIRED envelope (createdAt + ttl in the past)', () => {
    const past = new Date(Date.now() - 20 * 60 * 1000).toISOString(); // 20 min ago
    const env = makeEnvelope({ handoffId: 'h-old', createdAt: past, ttlSeconds: 900 }); // ttl 15 min
    writeHandoffEnvelope(env);
    expect(isHandoffExpired(env)).toBe(true);
    expect(readHandoffEnvelope('h-old')).toBeNull();
  });

  it('does not expire an envelope with a fresh timestamp', () => {
    const env = makeEnvelope({ handoffId: 'h-fresh' });
    expect(isHandoffExpired(env)).toBe(false);
    writeHandoffEnvelope(env);
    expect(readHandoffEnvelope('h-fresh')).not.toBeNull();
  });

  it('round-trips a result and validates the committed discriminant', () => {
    expect(writeHandoffResult('h-r', { committed: true, recordId: 'rec-1' })).toBe(true);
    expect(readHandoffResult('h-r')).toEqual({ committed: true, recordId: 'rec-1' });
    expect(readHandoffResult('h-none')).toBeNull();
  });

  it('clearHandoff removes BOTH the envelope and the result keys', () => {
    writeHandoffEnvelope(makeEnvelope({ handoffId: 'h-c' }));
    writeHandoffResult('h-c', { committed: false, cancelled: true });
    expect(sessionStorage.getItem('sprk.handoff.h-c')).toBeTruthy();
    expect(sessionStorage.getItem('sprk.handoff.h-c.result')).toBeTruthy();
    clearHandoff('h-c');
    expect(sessionStorage.getItem('sprk.handoff.h-c')).toBeNull();
    expect(sessionStorage.getItem('sprk.handoff.h-c.result')).toBeNull();
  });

  it('tolerates malformed JSON without throwing', () => {
    sessionStorage.setItem('sprk.handoff.h-bad', '{not json');
    expect(readHandoffEnvelope('h-bad')).toBeNull();
  });
});
