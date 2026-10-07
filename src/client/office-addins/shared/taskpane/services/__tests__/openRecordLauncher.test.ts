/**
 * Unit tests for openRecordLauncher (spaarkeai-word-add-in-r1 task 027, FR-10).
 *
 * Covers, per the task's required unit-test set:
 * - the launcher called with a canonical GUID (braces/whitespace/mixed-case stripped before the
 *   opener ever sees the URL — ADR-044)
 * - unset `ORG_URL` being a safe no-op with a stated reason (mirrors the Quick Create precedent)
 * - a missing record id being a safe no-op with a stated reason
 * - the default opener calling `Office.context.ui.openBrowserWindow`, never `window.open` / the
 *   Office Dialog API (Spike-2 Option 3)
 */

import {
  buildOpenRecordUrl,
  buildOpenSpaarkeUrl,
  configuredSpaarkeAppName,
  openDesktopUrl,
  openFileUrl,
  openRecord,
} from '../openRecordLauncher';

/** Runs `fn` with `SPAARKE_APP_NAME` set to `value` (`undefined` = unset), restoring it afterwards. */
function withAppName(value: string | undefined, fn: () => void): void {
  const original = process.env.SPAARKE_APP_NAME;
  if (value === undefined) delete process.env.SPAARKE_APP_NAME;
  else process.env.SPAARKE_APP_NAME = value;
  try {
    fn();
  } finally {
    if (original === undefined) delete process.env.SPAARKE_APP_NAME;
    else process.env.SPAARKE_APP_NAME = original;
  }
}

describe('record links name the Spaarke app (task 088 / UAT-1 — appname=)', () => {
  const ORG = 'https://contoso.crm.dynamics.com';
  const ID = '11111111-1111-1111-1111-111111111111';

  it('with the default build setting, opens {ORG_URL}/main.aspx?appname=sprk_MatterManagement&etn=sprk_document&id={id}&pagetype=entityrecord&navbar=off', () => {
    withAppName('sprk_MatterManagement', () => {
      const opener = jest.fn();
      const result = openRecord({ orgUrl: ORG, entityType: 'sprk_document', recordId: ID }, opener);

      expect(result).toEqual({ opened: true });
      expect(opener).toHaveBeenCalledWith(
        `${ORG}/main.aspx?appname=sprk_MatterManagement&etn=sprk_document&id=${ID}&pagetype=entityrecord&navbar=off`
      );
    });
  });

  it('with SPAARKE_APP_NAME empty (or unset), builds the same URL WITHOUT appname — never a guessed app', () => {
    for (const value of ['', '   ', undefined]) {
      withAppName(value, () => {
        const opener = jest.fn();
        openRecord({ orgUrl: ORG, entityType: 'sprk_document', recordId: ID }, opener);

        expect(opener).toHaveBeenCalledWith(
          `${ORG}/main.aspx?etn=sprk_document&id=${ID}&pagetype=entityrecord&navbar=off`
        );
        expect(configuredSpaarkeAppName()).toBe('');
      });
    }
  });

  it('URL-encodes the app name, and an explicit appName overrides the build setting (empty = no app)', () => {
    withAppName('sprk_MatterManagement', () => {
      expect(buildOpenRecordUrl(ORG, 'sprk_matter', ID, 'My App&x=1')).toBe(
        `${ORG}/main.aspx?appname=My%20App%26x%3D1&etn=sprk_matter&id=${ID}&pagetype=entityrecord&navbar=off`
      );

      const opener = jest.fn();
      openRecord({ orgUrl: ORG, entityType: 'sprk_matter', recordId: ID, appName: '' }, opener);
      expect(opener).toHaveBeenCalledWith(`${ORG}/main.aspx?etn=sprk_matter&id=${ID}&pagetype=entityrecord&navbar=off`);
    });
  });
});

describe('openFileUrl (task 088 / UAT-5 — the collision prompt opens the other file)', () => {
  const URL = 'https://contoso.sharepoint.com/_layouts/15/Doc.aspx?sourcedoc=x';

  it('uses openBrowserWindow when the host can open a browser window — and never window.open', () => {
    const browserWindow = jest.fn();
    const windowOpen = jest.fn();

    const result = openFileUrl(URL, true, { browserWindow, windowOpen });

    expect(result).toEqual({ opened: true });
    expect(browserWindow).toHaveBeenCalledWith(URL);
    expect(windowOpen).not.toHaveBeenCalled();
  });

  it('uses window.open(url, "_blank") when it cannot (Office on the web), and cuts the opener link', () => {
    const opened = { opener: {} as unknown } as Window;
    const browserWindow = jest.fn();
    const windowOpen = jest.fn().mockReturnValue(opened);

    const result = openFileUrl(URL, false, { browserWindow, windowOpen });

    expect(result).toEqual({ opened: true });
    expect(windowOpen).toHaveBeenCalledWith(URL, '_blank');
    expect(browserWindow).not.toHaveBeenCalled();
    expect(opened.opener).toBeNull();
  });

  it('reports a blocked window as not opened, with a reason — never a silent dead click', () => {
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const result = openFileUrl(URL, false, { windowOpen: jest.fn().mockReturnValue(null) });

      expect(result.opened).toBe(false);
      expect(result.reason).toMatch(/blocked the new window/i);
    } finally {
      warnSpy.mockRestore();
    }
  });
});

describe('openDesktopUrl (task 094 — the "Open in Word" UAT round 4 trial)', () => {
  const DESKTOP_URL = 'ms-word:https://contoso.sharepoint.com/contentstorage/x/Brief.docx';

  it('anchor-clicks the given url and reports opened: true', () => {
    const click = jest.fn();

    const result = openDesktopUrl(DESKTOP_URL, click);

    expect(result).toEqual({ opened: true });
    expect(click).toHaveBeenCalledTimes(1);
    const [anchor] = click.mock.calls[0]!;
    // The raw attribute, not the resolved `.href` getter — `ms-word:` is a non-standard scheme and
    // this avoids any URL-normalization pitfall in how jsdom serializes it back.
    expect(anchor.getAttribute('href')).toBe(DESKTOP_URL);
  });

  it('removes the anchor from the DOM after clicking it, even if the click throws', () => {
    const click = jest.fn(() => {
      throw new Error('boom');
    });

    expect(() => openDesktopUrl(DESKTOP_URL, click)).toThrow('boom');
    expect(document.querySelectorAll('a').length).toBe(0);
  });

  it('with the real (default) click — invokes HTMLAnchorElement.click() without throwing', () => {
    // jsdom implements click() as a no-op navigation; this just proves the default path is wired up.
    expect(() => openDesktopUrl(DESKTOP_URL)).not.toThrow();
  });
});

describe('buildOpenRecordUrl', () => {
  it('builds the main.aspx deep link for an existing record, focused (navbar=off, task 086 / FR-10)', () => {
    const url = buildOpenRecordUrl(
      'https://contoso.crm.dynamics.com',
      'sprk_matter',
      '11111111-1111-1111-1111-111111111111'
    );
    expect(url).toBe(
      'https://contoso.crm.dynamics.com/main.aspx?etn=sprk_matter&id=11111111-1111-1111-1111-111111111111&pagetype=entityrecord&navbar=off'
    );
  });

  it('never sets cmdbar=false — the command bar stays available (task 086 / FR-10 acceptance criterion)', () => {
    const url = buildOpenRecordUrl(
      'https://contoso.crm.dynamics.com',
      'sprk_matter',
      '11111111-1111-1111-1111-111111111111'
    );
    expect(url).not.toContain('cmdbar=false');
  });
});

describe('openRecord', () => {
  let warnSpy: jest.SpyInstance;

  beforeEach(() => {
    warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {
      /* no-op */
    });
  });

  afterEach(() => {
    warnSpy.mockRestore();
  });

  it('canonicalizes a braced, mixed-case, whitespace-padded GUID before the opener sees it (ADR-044)', () => {
    const opener = jest.fn();

    const result = openRecord(
      {
        orgUrl: 'https://contoso.crm.dynamics.com',
        entityType: 'sprk_document',
        recordId: '  {AAAA1111-BBBB-2222-CCCC-333344445555}  ',
      },
      opener
    );

    expect(result).toEqual({ opened: true });
    expect(opener).toHaveBeenCalledTimes(1);
    expect(opener).toHaveBeenCalledWith(
      'https://contoso.crm.dynamics.com/main.aspx?etn=sprk_document&id=aaaa1111-bbbb-2222-cccc-333344445555&pagetype=entityrecord&navbar=off'
    );
  });

  it('is a safe no-op with a stated reason when ORG_URL is unset — never opens a broken window', () => {
    const opener = jest.fn();

    const result = openRecord(
      {
        orgUrl: undefined,
        entityType: 'sprk_matter',
        recordId: '11111111-1111-1111-1111-111111111111',
      },
      opener
    );

    expect(result.opened).toBe(false);
    expect(result.reason).toBe('ORG_URL is not configured.');
    expect(opener).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('ORG_URL is not configured.'));
  });

  it('is a safe no-op with a stated reason when ORG_URL is an empty string', () => {
    const opener = jest.fn();

    const result = openRecord(
      { orgUrl: '', entityType: 'sprk_matter', recordId: '11111111-1111-1111-1111-111111111111' },
      opener
    );

    expect(result.opened).toBe(false);
    expect(result.reason).toBe('ORG_URL is not configured.');
    expect(opener).not.toHaveBeenCalled();
  });

  it('is a safe no-op with a stated reason when the record id is empty', () => {
    const opener = jest.fn();

    const result = openRecord(
      { orgUrl: 'https://contoso.crm.dynamics.com', entityType: 'sprk_matter', recordId: '' },
      opener
    );

    expect(result.opened).toBe(false);
    expect(result.reason).toBe('No record id was available to open.');
    expect(opener).not.toHaveBeenCalled();
  });

  it('defaults to Office.context.ui.openBrowserWindow — Spike-2 Option 3, never window.open', () => {
    const openBrowserWindowSpy = jest.fn();
    (global.Office.context.ui as unknown as { openBrowserWindow: jest.Mock }).openBrowserWindow = openBrowserWindowSpy;
    const windowOpenSpy = jest.spyOn(window, 'open').mockImplementation(() => null);

    const result = openRecord({
      orgUrl: 'https://contoso.crm.dynamics.com',
      entityType: 'sprk_document',
      recordId: '11111111-1111-1111-1111-111111111111',
    });

    expect(result.opened).toBe(true);
    expect(openBrowserWindowSpy).toHaveBeenCalledWith(
      'https://contoso.crm.dynamics.com/main.aspx?etn=sprk_document&id=11111111-1111-1111-1111-111111111111&pagetype=entityrecord&navbar=off'
    );
    expect(windowOpenSpy).not.toHaveBeenCalled();

    windowOpenSpy.mockRestore();
  });
});

describe('buildOpenSpaarkeUrl — the Word ribbon "Open Spaarke" (task 089, UAT-10)', () => {
  it('opens Matter Management on the Workspace (the Console) with the default build setting', () => {
    expect(buildOpenSpaarkeUrl('https://spaarkedev1.crm.dynamics.com', 'sprk_MatterManagement')).toBe(
      'https://spaarkedev1.crm.dynamics.com/main.aspx?appname=sprk_MatterManagement&pagetype=webresource&webresourceName=sprk_spaarkeai'
    );
  });

  it('encodes the app name and drops a trailing slash on the org URL', () => {
    expect(buildOpenSpaarkeUrl('https://org.crm.dynamics.com/', 'my app&x=1')).toBe(
      'https://org.crm.dynamics.com/main.aspx?appname=my%20app%26x%3D1&pagetype=webresource&webresourceName=sprk_spaarkeai'
    );
  });

  it('is null — never a guessed link — without ORG_URL, without an app name, or for a non-https org URL', () => {
    expect(buildOpenSpaarkeUrl(undefined, 'sprk_MatterManagement')).toBeNull();
    expect(buildOpenSpaarkeUrl('', 'sprk_MatterManagement')).toBeNull();
    expect(buildOpenSpaarkeUrl('https://org.crm.dynamics.com', '  ')).toBeNull();
    expect(buildOpenSpaarkeUrl('http://org.crm.dynamics.com', 'sprk_MatterManagement')).toBeNull();
    expect(buildOpenSpaarkeUrl('not a url', 'sprk_MatterManagement')).toBeNull();
  });

  it('carries nothing but the three settings-derived query values (no token, secret or BFF URL)', () => {
    const url = new URL(buildOpenSpaarkeUrl('https://org.crm.dynamics.com', 'sprk_MatterManagement')!);
    expect([...url.searchParams.keys()]).toEqual(['appname', 'pagetype', 'webresourceName']);
  });
});
