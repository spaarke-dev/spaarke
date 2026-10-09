/**
 * Task 120 (owner UAT round 12, task 119's finding): "Open in Spaarke" must work on Office on the web, where B2B
 * guests work and `OpenBrowserWindowApi` 1.1 is not supported.
 *
 * - `canOpenSpaarkeRecords` is the pane's ONE open gate: ORG_URL set AND (`canOpenBrowserWindow` OR `window.open`).
 * - `openRecord`'s default opener follows the host's actual support: `Office.context.ui.openBrowserWindow` on desktop,
 *   `window.open(url, '_blank')` on the web — with a blocked pop-up reported, never swallowed.
 */

import {
  buildOpenRecordUrl,
  canOpenSpaarkeRecords,
  isOpenBrowserWindowSupported,
  openInBrowserTab,
  openRecord,
} from '../openRecordLauncher';

const ORG = 'https://contoso.crm.dynamics.com';
const ID = '6fb8c3fa-13c3-f111-a05c-0022482913fc';

const officeContext = (): { requirements: { isSetSupported: jest.Mock }; ui: { openBrowserWindow: jest.Mock } } =>
  (
    global.Office as unknown as {
      context: { requirements: { isSetSupported: jest.Mock }; ui: { openBrowserWindow: jest.Mock } };
    }
  ).context;

/** Desktop: OpenBrowserWindowApi 1.1 supported. Web: not supported (every other set is). */
function host(kind: 'desktop' | 'web'): jest.Mock {
  const openBrowserWindow = jest.fn();
  officeContext().ui.openBrowserWindow = openBrowserWindow;
  officeContext().requirements.isSetSupported = jest.fn((name: string) =>
    kind === 'desktop' ? true : name !== 'OpenBrowserWindowApi'
  );
  return openBrowserWindow;
}

describe('canOpenSpaarkeRecords — the one open gate (task 120)', () => {
  it('is false without ORG_URL, whatever the host can do', () => {
    expect(canOpenSpaarkeRecords({ canOpenBrowserWindow: true }, undefined)).toBe(false);
    expect(canOpenSpaarkeRecords({ canOpenBrowserWindow: false }, '')).toBe(false);
  });

  it('is true on desktop (openBrowserWindow) with ORG_URL', () => {
    expect(canOpenSpaarkeRecords({ canOpenBrowserWindow: true }, ORG)).toBe(true);
  });

  it('is true on the web (no openBrowserWindow) because the pane can window.open', () => {
    expect(canOpenSpaarkeRecords({ canOpenBrowserWindow: false }, ORG)).toBe(true);
  });

  it('is false when neither opener exists', () => {
    const original = window.open;
    try {
      (window as unknown as { open: unknown }).open = undefined;
      expect(canOpenSpaarkeRecords({ canOpenBrowserWindow: false }, ORG)).toBe(false);
    } finally {
      window.open = original;
    }
  });
});

describe('openRecord default opener (task 120)', () => {
  const recordUrl = buildOpenRecordUrl(ORG, 'sprk_document', ID, process.env.SPAARKE_APP_NAME ?? '');

  it('on the web (no OpenBrowserWindowApi) opens the record URL with window.open in a new tab', () => {
    const openBrowserWindow = host('web');
    const popup = { opener: {} as unknown } as unknown as Window;
    const windowOpen = jest.spyOn(window, 'open').mockImplementation(() => popup);
    try {
      const result = openRecord({ orgUrl: ORG, entityType: 'sprk_document', recordId: `{${ID.toUpperCase()}}` });

      expect(result).toEqual({ opened: true });
      expect(windowOpen).toHaveBeenCalledWith(recordUrl, '_blank');
      expect(openBrowserWindow).not.toHaveBeenCalled();
      // The opened page cannot script the pane.
      expect(popup.opener).toBeNull();
    } finally {
      windowOpen.mockRestore();
    }
  });

  it('on desktop opens the record URL with Office.context.ui.openBrowserWindow and never window.open', () => {
    const openBrowserWindow = host('desktop');
    const windowOpen = jest.spyOn(window, 'open');
    try {
      const result = openRecord({ orgUrl: ORG, entityType: 'sprk_document', recordId: ID });

      expect(result).toEqual({ opened: true });
      expect(openBrowserWindow).toHaveBeenCalledWith(recordUrl);
      expect(windowOpen).not.toHaveBeenCalled();
    } finally {
      windowOpen.mockRestore();
    }
  });

  it('on the web reports a blocked pop-up instead of claiming it opened', () => {
    host('web');
    const windowOpen = jest.spyOn(window, 'open').mockImplementation(() => null);
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const result = openRecord({ orgUrl: ORG, entityType: 'sprk_matter', recordId: ID });

      expect(result.opened).toBe(false);
      expect(result.reason).toMatch(/blocked/i);
    } finally {
      windowOpen.mockRestore();
      warn.mockRestore();
    }
  });

  it('an injected opener that returns nothing is taken as opened (existing callers)', () => {
    const opener = jest.fn();
    expect(openRecord({ orgUrl: ORG, entityType: 'sprk_document', recordId: ID }, opener)).toEqual({ opened: true });
    expect(opener).toHaveBeenCalledWith(recordUrl);
  });
});

describe('isOpenBrowserWindowSupported / openInBrowserTab (task 120)', () => {
  it('treats a throwing requirement check as "not supported" and falls back to window.open', () => {
    officeContext().requirements.isSetSupported = jest.fn(() => {
      throw new Error('no requirements API');
    });
    const openBrowserWindow = jest.fn();
    officeContext().ui.openBrowserWindow = openBrowserWindow;
    const windowOpen = jest.spyOn(window, 'open').mockImplementation(() => ({ opener: {} }) as unknown as Window);
    try {
      expect(isOpenBrowserWindowSupported()).toBe(false);
      expect(openInBrowserTab('https://example.com/a')).toEqual({ opened: true });
      expect(windowOpen).toHaveBeenCalledWith('https://example.com/a', '_blank');
      expect(openBrowserWindow).not.toHaveBeenCalled();
    } finally {
      windowOpen.mockRestore();
    }
  });
});
