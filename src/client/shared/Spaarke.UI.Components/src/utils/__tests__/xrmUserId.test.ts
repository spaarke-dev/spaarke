/**
 * getXrmUserId — the Create*Wizard services' current-user lookup (task 081 round 6,
 * review R5-9). The pre-081 per-frame loops moved past a frame whose Xrm answered
 * with an EMPTY user id; round 5's shared lookup stopped at the first frame that
 * merely had `getGlobalContext` / `getUserId`. These pin master's behaviour.
 */
import { getXrmUserId, readXrmUserId } from '../xrmUserId';

const ID = 'A1B2C3D4-0000-4000-8000-000000000001';
const CLEAN = ID.toLowerCase();

const originalParent = window.parent;

afterEach(() => {
  delete (window as any).Xrm;
  Object.defineProperty(window, 'parent', { value: originalParent, writable: true, configurable: true });
});

function setParent(xrm: unknown): void {
  Object.defineProperty(window, 'parent', { value: { Xrm: xrm }, writable: true, configurable: true });
}

const withContextId = (userId: string) => ({ Utility: { getGlobalContext: () => ({ userSettings: { userId } }) } });

describe('getXrmUserId', () => {
  it('skips a frame whose getGlobalContext answers with an EMPTY id and uses the next frame', () => {
    (window as any).Xrm = withContextId('');
    setParent(withContextId(`{${ID}}`));
    expect(getXrmUserId()).toBe(CLEAN);
  });

  it('skips a frame whose getUserId answers with an empty id', () => {
    (window as any).Xrm = { Utility: { getUserId: () => '   ' } };
    setParent({ Utility: { getUserId: () => `{${ID}}` } });
    expect(getXrmUserId()).toBe(CLEAN);
  });

  it('prefers getGlobalContext, then falls back to getUserId in the SAME frame', () => {
    (window as any).Xrm = {
      Utility: { getGlobalContext: () => ({ userSettings: { userId: '' } }), getUserId: () => `{${ID}}` },
    };
    setParent(withContextId('{99999999-0000-4000-8000-000000000009}'));
    expect(getXrmUserId()).toBe(CLEAN);
  });

  it('a frame whose getter throws is skipped', () => {
    (window as any).Xrm = {
      Utility: {
        getGlobalContext: () => {
          throw new Error('host');
        },
      },
    };
    setParent(withContextId(ID));
    expect(getXrmUserId()).toBe(CLEAN);
  });

  it('returns undefined when no frame yields an id', () => {
    (window as any).Xrm = withContextId('');
    expect(getXrmUserId()).toBeUndefined();
  });
});

describe('readXrmUserId', () => {
  it.each([undefined, null, {}, { Utility: {} }])('no id from %p', xrm => {
    expect(readXrmUserId(xrm)).toBeUndefined();
  });
});
