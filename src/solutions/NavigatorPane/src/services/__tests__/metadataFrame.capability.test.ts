/**
 * Every NavigatorPane name-resolution helper reads `Xrm.Utility.getEntityMetadata`
 * from the nearest frame that HAS it (task 081 round 5 R4-1; pinned for every
 * helper in round 6, review R5-6).
 *
 * Metadata is optional to these helpers (no metadata -> an entity label, or the
 * entity is skipped), so it is looked up separately from the WebApi frame. The
 * setup below is the case that distinguishes the two: the pane's own window has
 * WebApi and a `Utility` WITHOUT `getEntityMetadata`; the parent frame has it.
 * Each helper must name the record "Acme Corp" (from the parent's metadata), not
 * fall back to the label. Reverting any one helper to a same-frame lookup fails
 * its row.
 *
 * The capture service's copy lives in `@spaarke/ui-components`
 * (`navigatorCaptureService.metadataFrame.test.ts`).
 */
import { liveSearchRecords } from '../liveSearchService';
import { listEditedByMe } from '../editedByMeService';
import { listMonitoredByMe } from '../monitoredService';
import { resolveRecordName, type MetadataUtility } from '../nameResolution';
import { addBookmark } from '../bookmarkService';

/* eslint-disable @typescript-eslint/no-explicit-any */

const OWNER_ID = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const RECORD_ID = 'a1a1a1a1-0000-4000-8000-000000000001';
const originalParent = window.parent;

afterEach(() => {
  delete (window as any).Xrm;
  Object.defineProperty(window, 'parent', { value: originalParent, writable: true, configurable: true });
});

/** The pane's own frame: WebApi + user context, but no getEntityMetadata. */
function installChildFrame() {
  const row = { accountid: RECORD_ID, name: 'Acme Corp', modifiedon: '2026-10-01T00:00:00Z' };
  const xrm = {
    WebApi: {
      retrieveMultipleRecords: jest.fn(async (entity: string) => ({ entities: entity === 'account' ? [row] : [] })),
      retrieveRecord: jest.fn(async (entity: string, id: string) =>
        entity === 'account' && id === RECORD_ID ? { name: 'Acme Corp' } : {}
      ),
      createRecord: jest.fn(async () => ({ id: 'pin-1' })),
      updateRecord: jest.fn(async () => ({})),
      deleteRecord: jest.fn(async () => ({})),
    },
    Utility: { getGlobalContext: () => ({ userSettings: { userId: OWNER_ID } }) },
  };
  (window as any).Xrm = xrm;
  return xrm;
}

/** The parent frame: the only one that can answer getEntityMetadata. */
function installParentMetadata() {
  const getEntityMetadata = jest.fn(async () => ({ PrimaryNameAttribute: 'name' }));
  Object.defineProperty(window, 'parent', {
    value: { Xrm: { Utility: { getEntityMetadata } } },
    writable: true,
    configurable: true,
  });
  return getEntityMetadata;
}

type Child = ReturnType<typeof installChildFrame>;

const HELPERS: Array<[string, (child: Child) => Promise<string | undefined>]> = [
  ['liveSearchService.liveSearchRecords', async () => (await liveSearchRecords('acme', ['account']))[0]?.label],
  [
    'editedByMeService.listEditedByMe',
    async () => (await listEditedByMe({ entities: ['account'] }))[0]?.displayName,
  ],
  [
    'monitoredService.listMonitoredByMe',
    async () => (await listMonitoredByMe({ entities: ['account'] }))[0]?.displayName,
  ],
  [
    'nameResolution.resolveRecordName (default metadata source)',
    async child => (await resolveRecordName(child as any, 'account', RECORD_ID)) ?? undefined,
  ],
  [
    'bookmarkService.addBookmark (record URL)',
    async child => {
      await addBookmark(
        OWNER_ID,
        `https://spaarkedev1.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=account&id=${RECORD_ID}`
      );
      const created = (child.WebApi.createRecord.mock.calls as unknown[][]).map(c => c[1] as any);
      return created.find(d => d?.sprk_targetid === RECORD_ID)?.sprk_displayname;
    },
  ],
];

describe('NavigatorPane name helpers — getEntityMetadata from the frame that has it', () => {
  it.each(HELPERS)('%s names the record from the parent frame metadata', async (_name, run) => {
    const child = installChildFrame();
    const getEntityMetadata = installParentMetadata();

    expect(await run(child)).toBe('Acme Corp');
    expect(getEntityMetadata).toHaveBeenCalledWith('account');
  });

  it('live search with no frame able to answer getEntityMetadata skips the entity (unchanged fallback)', async () => {
    const child = installChildFrame();
    expect(await liveSearchRecords('acme', ['account'])).toEqual([]);
    expect(child.WebApi.retrieveMultipleRecords).not.toHaveBeenCalled();
  });
});

describe('nameResolution.resolveRecordName — injected metadata source (review R5-8)', () => {
  it('uses the injected metadata source, not the host walk', async () => {
    const child = installChildFrame();
    const hostMetadata = installParentMetadata();
    const injected: MetadataUtility = {
      getEntityMetadata: jest.fn(async () => ({ PrimaryNameAttribute: 'name' })),
    } as unknown as MetadataUtility;

    expect(await resolveRecordName(child as any, 'account', RECORD_ID, injected)).toBe('Acme Corp');
    expect(injected.getEntityMetadata).toHaveBeenCalledWith('account');
    expect(hostMetadata).not.toHaveBeenCalled();
  });

  it('an injected source without getEntityMetadata resolves nothing (no fallback to the walk)', async () => {
    const child = installChildFrame();
    const hostMetadata = installParentMetadata();

    expect(await resolveRecordName(child as any, 'account', RECORD_ID, {} as MetadataUtility)).toBeNull();
    expect(hostMetadata).not.toHaveBeenCalled();
  });
});
