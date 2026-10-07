/**
 * Embedded Daily Briefing "Browse Playbooks" handler (task 081 / C-8).
 *
 * The registration factory hands `DailyBriefingApp` an `onBrowsePlaybooks`
 * thunk that opens the Playbook Library through the host `Xrm.Navigation`.
 * Two defects fixed when its inline frame walk was converged onto the shared
 * `getXrm()` (the REAL walker — the package's jest mock re-exports it from
 * the UI.Components source; only the `window` frames / Xrm object are faked):
 *   1. `navigateTo` was detached into a local and called unbound (the R7 W12
 *      rule: call it as a method on `Navigation`).
 *   2. `w.Xrm ?? w.parent?.Xrm ?? w.top?.Xrm` had no try, so a cross-origin
 *      parent threw out of the click handler before `top` was tried.
 */
import * as React from 'react';

jest.mock('../src/components/DailyBriefingApp', () => ({
  DailyBriefingApp: () => null,
}));

import { createDailyBriefingRegistration } from '../src/widgets/dailyBriefing.registration';

type BrowseProps = { onBrowsePlaybooks: () => void };

function getBrowseHandler(): () => void {
  const config = createDailyBriefingRegistration().factory({} as never);
  const element = (config as unknown as { renderContent: () => React.ReactElement<BrowseProps> }).renderContent();
  return element.props.onBrowsePlaybooks;
}

/** jsdom: `window.top` is unforgeable; its getter reads the internal `_top`. */
function setWindowTop(value: unknown): void {
  (window as unknown as { _top: unknown })._top = value;
}

describe('createDailyBriefingRegistration — onBrowsePlaybooks', () => {
  const originalParent = window.parent;
  const originalTop = window.top;

  afterEach(() => {
    delete (window as unknown as { Xrm?: unknown }).Xrm;
    Object.defineProperty(window, 'parent', { value: originalParent, writable: true, configurable: true });
    setWindowTop(originalTop);
  });

  function makeXrm() {
    const Navigation = {
      navigateTo: jest.fn(function (this: unknown, ..._args: unknown[]) {
        // Real Xrm.Navigation.navigateTo depends on its receiver.
        if (this !== Navigation) {
          return Promise.reject(new Error('navigateTo called unbound'));
        }
        return Promise.resolve(undefined);
      }),
    };
    return { WebApi: {}, Navigation };
  }

  it('calls Xrm.Navigation.navigateTo as a method (bound) with the Playbook Library page', async () => {
    const xrm = makeXrm();
    (window as unknown as { Xrm: unknown }).Xrm = xrm;

    getBrowseHandler()();

    expect(xrm.Navigation.navigateTo).toHaveBeenCalledTimes(1);
    expect(xrm.Navigation.navigateTo.mock.contexts[0]).toBe(xrm.Navigation);
    expect(xrm.Navigation.navigateTo.mock.calls[0][0]).toMatchObject({
      pageType: 'webresource',
      webresourceName: 'sprk_playbooklibrary',
    });
    await expect(xrm.Navigation.navigateTo.mock.results[0].value).resolves.toBeUndefined();
  });

  it('reaches top.Xrm when reading the parent frame throws (cross-origin)', () => {
    const crossOriginParent = {};
    Object.defineProperty(crossOriginParent, 'Xrm', {
      get() {
        throw new DOMException('Blocked a frame with origin', 'SecurityError');
      },
    });
    Object.defineProperty(window, 'parent', { value: crossOriginParent, writable: true, configurable: true });
    const xrm = makeXrm();
    setWindowTop({ Xrm: xrm });

    expect(() => getBrowseHandler()()).not.toThrow();
    expect(xrm.Navigation.navigateTo).toHaveBeenCalledTimes(1);
  });

  it('warns and does nothing when no frame has Xrm', () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    expect(() => getBrowseHandler()()).not.toThrow();
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('Xrm.Navigation unavailable'));
    warn.mockRestore();
  });
});
