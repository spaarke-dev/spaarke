/**
 * liveSearchRecords — Xrm.Utility.getEntityMetadata comes from the nearest frame
 * that HAS it (task 081 round 5, review R4-1).
 *
 * The search needs each entity's primary-name attribute (from
 * `getEntityMetadata`) before it can build its `contains()` filter. It read that
 * from the same frame as WebApi, so a child frame whose Utility lacks
 * `getEntityMetadata` produced no results even though the parent frame could
 * answer. Metadata is optional (no metadata → the entity is skipped, as before),
 * so it is resolved separately from the WebApi frame rather than required of it.
 */
import { liveSearchRecords } from '../liveSearchService';

/* eslint-disable @typescript-eslint/no-explicit-any */

const originalParent = window.parent;

afterEach(() => {
  delete (window as any).Xrm;
  Object.defineProperty(window, 'parent', { value: originalParent, writable: true, configurable: true });
});

function setParent(xrm: unknown): void {
  Object.defineProperty(window, 'parent', { value: { Xrm: xrm }, writable: true, configurable: true });
}

describe('liveSearchRecords — metadata frame', () => {
  it('uses getEntityMetadata from the parent frame when the child frame lacks it', async () => {
    const retrieve = jest.fn(async () => ({ entities: [{ accountid: 'a1', name: 'Acme Corp' }] }));
    (window as any).Xrm = { WebApi: { retrieveMultipleRecords: retrieve }, Utility: {} };
    const getEntityMetadata = jest.fn(async () => ({ PrimaryNameAttribute: 'name' }));
    setParent({ WebApi: { retrieveMultipleRecords: jest.fn() }, Utility: { getEntityMetadata } });

    const results = await liveSearchRecords('acme', ['account']);

    expect(getEntityMetadata).toHaveBeenCalledWith('account');
    expect(retrieve).toHaveBeenCalledTimes(1);
    expect(String((retrieve.mock.calls[0] as unknown[])[1])).toContain('contains(name,');
    expect(results.map(r => r.label)).toEqual(['Acme Corp']);
  });

  it('with no frame able to answer getEntityMetadata the entity is skipped (unchanged fallback)', async () => {
    const retrieve = jest.fn(async () => ({ entities: [] }));
    (window as any).Xrm = { WebApi: { retrieveMultipleRecords: retrieve }, Utility: {} };
    expect(await liveSearchRecords('acme', ['account'])).toEqual([]);
    expect(retrieve).not.toHaveBeenCalled();
  });
});
