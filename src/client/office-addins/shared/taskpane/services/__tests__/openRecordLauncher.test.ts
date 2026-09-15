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

import { buildOpenRecordUrl, openRecord } from '../openRecordLauncher';

describe('buildOpenRecordUrl', () => {
  it('builds the main.aspx deep link for an existing record', () => {
    const url = buildOpenRecordUrl(
      'https://contoso.crm.dynamics.com',
      'sprk_matter',
      '11111111-1111-1111-1111-111111111111'
    );
    expect(url).toBe(
      'https://contoso.crm.dynamics.com/main.aspx?etn=sprk_matter&id=11111111-1111-1111-1111-111111111111&pagetype=entityrecord'
    );
  });
});

describe('openRecord', () => {
  let warnSpy: jest.SpyInstance;

  beforeEach(() => {
    warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
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
      'https://contoso.crm.dynamics.com/main.aspx?etn=sprk_document&id=aaaa1111-bbbb-2222-cccc-333344445555&pagetype=entityrecord'
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
      'https://contoso.crm.dynamics.com/main.aspx?etn=sprk_document&id=11111111-1111-1111-1111-111111111111&pagetype=entityrecord'
    );
    expect(windowOpenSpy).not.toHaveBeenCalled();

    windowOpenSpy.mockRestore();
  });
});
