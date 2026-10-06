/**
 * openPlaybookLibrary — the one "Browse Playbooks" opener for both Daily
 * Briefing hosts (task 081 round 4, review F6).
 *
 * The standalone Code Page (`src/solutions/DailyBriefing/src/main.tsx`) used
 * to detach `navigateTo` into a local and call it unbound; the real
 * `Xrm.Navigation.navigateTo` depends on its receiver. The stub below rejects
 * an unbound call, as the platform does.
 */
import * as React from 'react';
import { openPlaybookLibrary } from '../src/utils/openPlaybookLibrary';
import { browsePlaybooks } from '../../../../solutions/DailyBriefing/src/browsePlaybooks';

jest.mock('../src/components/DailyBriefingApp', () => ({ DailyBriefingApp: () => null }));
// eslint-disable-next-line import/first
import { createDailyBriefingRegistration } from '../src/widgets/dailyBriefing.registration';

function registrationBrowseHandler(): () => void {
  const config = createDailyBriefingRegistration().factory({} as never);
  const element = (
    config as unknown as { renderContent: () => React.ReactElement<{ onBrowsePlaybooks: () => void }> }
  ).renderContent();
  return element.props.onBrowsePlaybooks;
}

function makeXrm() {
  const Navigation = {
    navigateTo: jest.fn(function (this: unknown, ..._args: unknown[]) {
      if (this !== Navigation) return Promise.reject(new Error('navigateTo called unbound'));
      return Promise.resolve(undefined);
    }),
  };
  return { WebApi: {}, Navigation };
}

describe('openPlaybookLibrary', () => {
  const originalParent = window.parent;
  afterEach(() => {
    delete (window as unknown as { Xrm?: unknown }).Xrm;
    Object.defineProperty(window, 'parent', { value: originalParent, writable: true, configurable: true });
  });

  it('calls Xrm.Navigation.navigateTo as a method with the Playbook Library page', async () => {
    const xrm = makeXrm();
    (window as unknown as { Xrm: unknown }).Xrm = xrm;
    openPlaybookLibrary('[test]');
    expect(xrm.Navigation.navigateTo).toHaveBeenCalledTimes(1);
    expect(xrm.Navigation.navigateTo.mock.contexts[0]).toBe(xrm.Navigation);
    expect(xrm.Navigation.navigateTo.mock.calls[0][0]).toMatchObject({
      pageType: 'webresource',
      webresourceName: 'sprk_playbooklibrary',
    });
    await expect(xrm.Navigation.navigateTo.mock.results[0].value).resolves.toBeUndefined();
  });

  it('uses the parent frame when the window has no Xrm that can navigate', () => {
    const xrm = makeXrm();
    (window as unknown as { Xrm: unknown }).Xrm = { WebApi: {} };
    Object.defineProperty(window, 'parent', { value: { Xrm: xrm }, writable: true, configurable: true });
    openPlaybookLibrary('[test]');
    expect(xrm.Navigation.navigateTo).toHaveBeenCalledTimes(1);
  });

  it('warns and does nothing when no frame can navigate', () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    expect(() => openPlaybookLibrary('[test]')).not.toThrow();
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('[test] Xrm.Navigation unavailable'));
    warn.mockRestore();
  });

  // Behavioural (round 5, R4-8): each host's handler, invoked as the UI would
  // invoke it, must call navigateTo bound to Navigation. The Code Page's handler
  // is the module `main.tsx` passes as `onBrowsePlaybooks` (main.tsx itself
  // cannot be imported under jest: import.meta.env).
  it.each([
    ['standalone Code Page (browsePlaybooks)', () => browsePlaybooks()],
    ['embedded registration (onBrowsePlaybooks)', () => registrationBrowseHandler()()],
  ])('%s calls Xrm.Navigation.navigateTo as a method', async (_host, invoke) => {
    const xrm = makeXrm();
    (window as unknown as { Xrm: unknown }).Xrm = xrm;
    invoke();
    expect(xrm.Navigation.navigateTo).toHaveBeenCalledTimes(1);
    expect(xrm.Navigation.navigateTo.mock.contexts[0]).toBe(xrm.Navigation);
    expect(xrm.Navigation.navigateTo.mock.calls[0][0]).toMatchObject({ webresourceName: 'sprk_playbooklibrary' });
    await expect(xrm.Navigation.navigateTo.mock.results[0].value).resolves.toBeUndefined();
  });
});
