/**
 * field-mapping-server-write-path-r1 task 045f part B: the "Parent" button opens only a same-origin relative record
 * URL. The stored regarding record URL is opened when it is one; any other value never reaches `Xrm.Navigation.openUrl`
 * (or a window location) and the URL is built from the row's pair instead; with neither, nothing is opened.
 */

import { navigateToParentRecord, openParentRecord, resolveParentRecordUrl } from '../sidePaneService';

const MATTER_ID = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
const OTHER_ID = '11111111-2222-3333-4444-555555555555';
const TYPE_ID = 'aaaaaaaa-0000-4000-8000-000000000001';
const CANONICAL = `/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=${MATTER_ID}`;

const openUrl = jest.fn();
const retrieveRecord = jest.fn();

beforeEach(() => {
  openUrl.mockReset();
  retrieveRecord.mockReset();
  retrieveRecord.mockResolvedValue({ sprk_recordlogicalname: 'sprk_matter' });
  jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  jest.spyOn(console, 'error').mockImplementation(() => undefined);
  (globalThis as unknown as { window: unknown }).window = {
    Xrm: { Navigation: { openUrl }, WebApi: { retrieveRecord } },
  };
  (globalThis as unknown as { window: { parent: unknown } }).window.parent = (globalThis as unknown as { window: unknown })
    .window;
});

afterEach(() => {
  jest.restoreAllMocks();
  delete (globalThis as unknown as { window?: unknown }).window;
});

describe('navigateToParentRecord', () => {
  it('opens a same-origin relative record URL', () => {
    expect(navigateToParentRecord(CANONICAL)).toBe(true);
    expect(openUrl).toHaveBeenCalledWith({ url: CANONICAL });
  });

  it.each([
    ['another host', `https://evil.example/main.aspx?etn=sprk_matter&id=${MATTER_ID}`],
    ['a javascript scheme', 'javascript:alert(1)'],
    ['a data scheme', 'data:text/html,x'],
    ['a protocol-relative URL', `//evil.example/main.aspx?etn=sprk_matter&id=${MATTER_ID}`],
    ['a URL with no record', '/main.aspx?pagetype=entityrecord'],
    ['an empty value', ''],
  ])('never opens %s', (_label, stored) => {
    expect(navigateToParentRecord(stored)).toBe(false);
    expect(openUrl).not.toHaveBeenCalled();
  });
});

describe('openParentRecord', () => {
  it('opens the stored URL when it is a same-origin relative record URL naming the pair, without a lookup', async () => {
    await expect(openParentRecord(CANONICAL, TYPE_ID, MATTER_ID)).resolves.toBe(true);
    expect(openUrl).toHaveBeenCalledWith({ url: CANONICAL });
    expect(retrieveRecord).not.toHaveBeenCalled();
  });

  it.each([
    ['another host', `https://evil.example/main.aspx?pagetype=entityrecord&etn=contact&id=${OTHER_ID}`],
    ['an absolute URL on the org host', `https://org.crm.dynamics.com/main.aspx?etn=sprk_matter&id=${MATTER_ID}`],
    ['a javascript scheme', 'javascript:alert(1)'],
    ['a protocol-relative URL', `//evil.example/main.aspx?etn=sprk_matter&id=${MATTER_ID}`],
    ['a relative URL naming another record', `/main.aspx?etn=contact&id=${OTHER_ID}`],
  ])('never opens %s; opens the URL built from the pair instead', async (_label, stored) => {
    await expect(openParentRecord(stored, TYPE_ID, MATTER_ID)).resolves.toBe(true);
    expect(openUrl).toHaveBeenCalledTimes(1);
    expect(openUrl).toHaveBeenCalledWith({ url: CANONICAL });
    expect(retrieveRecord).toHaveBeenCalledWith('sprk_recordtype_ref', TYPE_ID, '?$select=sprk_recordlogicalname');
  });

  it('opens the URL built from the pair when nothing is stored', async () => {
    await expect(openParentRecord(undefined, TYPE_ID, `{${MATTER_ID.toUpperCase()}}`)).resolves.toBe(true);
    expect(openUrl).toHaveBeenCalledWith({ url: CANONICAL });
  });

  it('opens nothing when the stored value is refused and the pair table cannot be read', async () => {
    retrieveRecord.mockRejectedValue(new Error('403'));
    await expect(openParentRecord('javascript:alert(1)', TYPE_ID, MATTER_ID)).resolves.toBe(false);
    expect(openUrl).not.toHaveBeenCalled();
  });

  it('opens nothing when the stored value is refused and there is no pair', async () => {
    await expect(openParentRecord('https://evil.example/', null, null)).resolves.toBe(false);
    expect(openUrl).not.toHaveBeenCalled();
  });

  it('resolveParentRecordUrl returns null instead of an off-origin value', async () => {
    await expect(resolveParentRecordUrl('https://evil.example/', undefined, undefined)).resolves.toBeNull();
  });
});
