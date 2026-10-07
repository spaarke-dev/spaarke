/**
 * navigatorCaptureService — the history row's record name comes from
 * `Xrm.Utility.getEntityMetadata` on the nearest frame that HAS it (task 081
 * round 5 R4-1; pinned in round 6, review R5-6). Companion to NavigatorPane's
 * `metadataFrame.capability.test.ts`, which covers the pane's own helpers.
 *
 * Setup: the pane's own window has WebApi, getGlobalContext and getPageContext
 * (everything the tick requests) but NO getEntityMetadata; the parent frame has
 * it. The captured row must be named from the record ("Acme Corp"), not fall
 * back to the entity label ("Matter").
 */
import { startNavigatorCapture } from '../navigatorCaptureService';

const OWNER_ID = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const MATTER_ID = '11111111-1111-1111-1111-111111111111';
const originalParent = window.parent;

describe('navigatorCaptureService — record name from the frame with getEntityMetadata', () => {
  let stop: (() => void) | undefined;

  beforeEach(() => jest.useFakeTimers());

  afterEach(() => {
    stop?.();
    stop = undefined;
    jest.useRealTimers();
    delete (window as any).Xrm;
    Object.defineProperty(window, 'parent', { value: originalParent, writable: true, configurable: true });
  });

  it('names the captured history row from the parent frame metadata', async () => {
    const createRecord = jest.fn(async () => ({ id: 'navitem-1' }));
    (window as any).Xrm = {
      WebApi: {
        retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
        retrieveRecord: jest.fn(async () => ({ sprk_name: 'Acme Corp' })),
        createRecord,
        updateRecord: jest.fn(async () => ({})),
        deleteRecord: jest.fn(async () => ({})),
      },
      Utility: {
        getGlobalContext: () => ({ userSettings: { userId: OWNER_ID } }),
        getPageContext: () => ({ input: { pageType: 'entityrecord', entityName: 'sprk_matter', entityId: MATTER_ID } }),
      },
    };
    const getEntityMetadata = jest.fn(async () => ({ PrimaryNameAttribute: 'sprk_name' }));
    Object.defineProperty(window, 'parent', {
      value: { Xrm: { Utility: { getEntityMetadata } } },
      writable: true,
      configurable: true,
    });

    stop = startNavigatorCapture();
    await jest.advanceTimersByTimeAsync(0);

    expect(getEntityMetadata).toHaveBeenCalledWith('sprk_matter');
    const created = (createRecord.mock.calls as unknown[][]).map(c => c[1] as any);
    expect(created.find(d => d?.sprk_targetid === MATTER_ID)?.sprk_displayname).toBe('Acme Corp');
  });
});
