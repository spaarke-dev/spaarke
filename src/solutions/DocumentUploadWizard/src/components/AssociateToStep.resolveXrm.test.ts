/**
 * resolveXrm — per-frame feature check (task 081 round 3, review M2).
 *
 * The step needs BOTH `WebApi.retrieveMultipleRecords` and
 * `Utility.lookupObjects`. Before task 081 its own frame walk checked both on
 * every frame; round 2 took the first frame with any WebApi and only then
 * checked, so a WebApi-only child frame made the step report "no Xrm" even
 * though the parent frame had both. The check is now applied per frame by the
 * shared walker.
 *
 * This jest config runs in the `node` environment, so a minimal `window`
 * global stands in for the frame chain.
 */

jest.mock('@spaarke/ui-components', () => ({
  cleanGuid: (id: string) => id,
  // The REAL shared cross-frame walker, from source.
  getXrm: jest.requireActual('../../../../client/shared/Spaarke.UI.Components/src/utils/xrmContext').getXrm,
}));
jest.mock('../services/uploadOrchestrator', () => ({ SUPPORTED_ENTITY_TYPES: [] }));

import { resolveXrm } from './AssociateToStep';

const full = {
  source: 'parent',
  WebApi: { retrieveMultipleRecords: jest.fn() },
  Utility: { lookupObjects: jest.fn() },
};

function setFrames(windowXrm: unknown, parentXrm: unknown): void {
  const parent: Record<string, unknown> = { Xrm: parentXrm };
  parent.parent = parent;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (global as any).window = { Xrm: windowXrm, parent, top: parent };
}

afterEach(() => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  delete (global as any).window;
});

describe('AssociateToStep resolveXrm', () => {
  it('skips a WebApi-only child frame and returns the parent frame that also has lookupObjects', () => {
    setFrames({ WebApi: { retrieveMultipleRecords: jest.fn() } }, full);
    expect(resolveXrm()).toBe(full);
  });

  it('returns null when no frame has both capabilities', () => {
    setFrames({ WebApi: { retrieveMultipleRecords: jest.fn() } }, { WebApi: {} });
    expect(resolveXrm()).toBeNull();
  });
});
