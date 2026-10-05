/**
 * getClientUrl — per-frame feature check (task 081 round 3, review M2).
 *
 * Before task 081 this function fell back to its own frame walk looking for a
 * frame whose Xrm could return a client URL. Round 2 replaced it with the
 * shared `getXrm()` (first frame with WebApi) and dropped the fallback, so a
 * child frame carrying a WebApi-only Xrm hid the parent's Utility and the
 * function returned null. It now asks for the 'clientUrl' capability, which the
 * shared walker checks per frame.
 */

jest.mock('@spaarke/ui-components', () => ({
  cleanGuid: (id: string | null | undefined) => (id ?? '').replace(/[{}]/g, '').trim().toLowerCase(),
  // The REAL shared cross-frame walker.
  getXrm: jest.requireActual('@spaarke/ui-components/utils/xrmContext').getXrm,
}));

import { getClientUrl } from '../xrmAccess';

describe('SmartTodo getClientUrl', () => {
  const originalParent = window.parent;

  afterEach(() => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (window as any).Xrm;
    Object.defineProperty(window, 'parent', { value: originalParent, writable: true, configurable: true });
  });

  it('skips a child frame whose Xrm has WebApi but no Utility and uses the parent', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).Xrm = { WebApi: {} };
    Object.defineProperty(window, 'parent', {
      value: {
        Xrm: { WebApi: {}, Utility: { getGlobalContext: () => ({ getClientUrl: () => 'https://org.crm.dynamics.com' }) } },
      },
      writable: true,
      configurable: true,
    });
    expect(getClientUrl()).toBe('https://org.crm.dynamics.com');
  });

  it('returns null when no frame can provide a client URL', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).Xrm = { WebApi: {} };
    expect(getClientUrl()).toBeNull();
  });
});
