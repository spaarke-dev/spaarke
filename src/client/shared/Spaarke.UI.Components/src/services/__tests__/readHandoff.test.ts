/**
 * readHandoff.test.ts — the launched-surface read side (task 012, design §1 steps 4–6, 8–9).
 * Covers: read id from URL data envelope, envelope hydration, seed extraction,
 * committed/cancelled result writes, and the "opened directly" (no handoff) branch.
 */
import {
  readHandoffFromUrl,
  handoffSeed,
  completeHandoff,
  completeOrClose,
  cancelHandoff,
} from '../surfaceHandoff/readHandoff';
import { writeHandoffEnvelope, readHandoffResult, DEFAULT_HANDOFF_TTL_SECONDS } from '../surfaceHandoff/handoffStorage';
import type { SurfaceHandoffEnvelope } from '../surfaceHandoff/types';

function makeEnvelope(id: string, over: Partial<SurfaceHandoffEnvelope> = {}): SurfaceHandoffEnvelope {
  return {
    handoffId: id,
    source: { consumerType: 'create-matter' },
    target: { surface: 'sprk_creatematterwizard', kind: 'wizard' },
    fileIds: ['spe-1'],
    draftValues: { sprk_mattername: 'Acme' },
    resolvedLookups: { sprk_practicearea: { recordId: 'pa-1', confidence: 'high' } },
    provenance: {},
    createdAt: new Date().toISOString(),
    ttlSeconds: DEFAULT_HANDOFF_TTL_SECONDS,
    ...over,
  };
}

describe('readHandoffFromUrl', () => {
  beforeEach(() => sessionStorage.clear());

  it('reads the handoff id from the Xrm data envelope and hydrates the stored envelope', () => {
    writeHandoffEnvelope(makeEnvelope('h-42'));
    // Xrm launch shape: ?data=<url-encoded key=val&key=val>
    const search = '?data=' + encodeURIComponent('handoffId=h-42&bffBaseUrl=https://bff');
    const ctx = readHandoffFromUrl(search);
    expect(ctx).not.toBeNull();
    expect(ctx!.handoffId).toBe('h-42');
    expect(ctx!.envelope).not.toBeNull();
    expect(ctx!.envelope!.draftValues).toEqual({ sprk_mattername: 'Acme' });
  });

  it('reads the handoff id from raw query params too', () => {
    writeHandoffEnvelope(makeEnvelope('h-raw'));
    const ctx = readHandoffFromUrl('?handoffId=h-raw&bffBaseUrl=x');
    expect(ctx!.handoffId).toBe('h-raw');
    expect(ctx!.envelope).not.toBeNull();
  });

  it('returns null when the URL carries no handoffId (opened directly, not a hand-off)', () => {
    expect(readHandoffFromUrl('?bffBaseUrl=https://bff')).toBeNull();
    expect(readHandoffFromUrl('')).toBeNull();
  });

  it('returns a context with null envelope when the id is present but storage was not shared', () => {
    // Simulates the sandboxed-iframe fallback: id round-trips on the URL, but the
    // envelope was never readable in this context.
    const ctx = readHandoffFromUrl('?handoffId=h-missing');
    expect(ctx).not.toBeNull();
    expect(ctx!.handoffId).toBe('h-missing');
    expect(ctx!.envelope).toBeNull();
  });
});

describe('handoffSeed', () => {
  beforeEach(() => sessionStorage.clear());

  it('extracts draftValues + resolvedLookups + fileIds from a hydrated context', () => {
    writeHandoffEnvelope(makeEnvelope('h-s'));
    const ctx = readHandoffFromUrl('?handoffId=h-s');
    const seed = handoffSeed(ctx);
    expect(seed).not.toBeNull();
    expect(seed!.draftValues).toEqual({ sprk_mattername: 'Acme' });
    expect(seed!.resolvedLookups).toEqual({ sprk_practicearea: { recordId: 'pa-1', confidence: 'high' } });
    expect(seed!.fileIds).toEqual(['spe-1']);
  });

  it('returns null when there is no envelope or nothing to seed', () => {
    expect(handoffSeed(null)).toBeNull();
    writeHandoffEnvelope(makeEnvelope('h-empty', { draftValues: {}, resolvedLookups: {}, fileIds: [] }));
    const ctx = readHandoffFromUrl('?handoffId=h-empty');
    expect(handoffSeed(ctx)).toBeNull();
  });
});

describe('completeHandoff / cancelHandoff (return path)', () => {
  beforeEach(() => sessionStorage.clear());

  it('completeHandoff writes a committed result the Assistant can read', () => {
    completeHandoff('h-done', 'rec-7');
    expect(readHandoffResult('h-done')).toEqual({ committed: true, recordId: 'rec-7' });
  });

  it('cancelHandoff writes an explicit cancelled result', () => {
    cancelHandoff('h-x');
    expect(readHandoffResult('h-x')).toEqual({ committed: false, cancelled: true });
  });
});

describe('completeOrClose (D-013-04 honest-ack wiring)', () => {
  it('routes a launched wizard SUCCESS through onComplete(recordId), not onClose', () => {
    const onClose = jest.fn();
    const onComplete = jest.fn();
    completeOrClose('matter-99', onClose, onComplete);
    expect(onComplete).toHaveBeenCalledTimes(1);
    expect(onComplete).toHaveBeenCalledWith('matter-99');
    expect(onClose).not.toHaveBeenCalled();
  });

  it('falls back to onClose when the wizard was opened directly (no onComplete)', () => {
    const onClose = jest.fn();
    completeOrClose('matter-99', onClose, undefined);
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
