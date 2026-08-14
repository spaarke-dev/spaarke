/**
 * navItemRepository.renameNavItem Tests (UAT fix #4 — inline rename)
 *
 * `renameNavItem` is an additive `@spaarke/ui-components` repo function
 * (spaarke-side-pane-navigation-history-r1) — `BookmarksTab.tsx`'s hover
 * pencil calls it directly. Verifies:
 *   - Calls `Xrm.WebApi.updateRecord('sprk_navitem', id, { sprk_displayname })`
 *     with EXACTLY that payload (no other field touched).
 *   - Any `Xrm.WebApi` failure is wrapped in {@link NavItemRepositoryError},
 *     mirroring every other write in this module (`createPinItem`,
 *     `deleteNavItem`, etc.).
 *   - "Xrm not available" also throws {@link NavItemRepositoryError} (the
 *     module's universal `requireWebApi` guard).
 *
 * @see ../../../../client/shared/Spaarke.UI.Components/src/services/navigator/navItemRepository.ts
 * @see ../../tabs/__tests__/BookmarksTab.test.tsx — the UI-level rename interaction this repo fn backs
 */

import {
  renameNavItem,
  NavItemRepositoryError,
} from '@spaarke/ui-components/services/navigator/navItemRepository';

const NAVITEM_ENTITY = 'sprk_navitem';
const NAV_ITEM_ID = 'pin-1';

function installMockXrm(options: { updateRecord?: jest.Mock } = {}) {
  const updateRecord = options.updateRecord ?? jest.fn(async () => ({}));
  const fakeXrm = {
    WebApi: {
      retrieveMultipleRecords: jest.fn(),
      retrieveRecord: jest.fn(),
      createRecord: jest.fn(),
      updateRecord,
      deleteRecord: jest.fn(),
    },
  };
  (window as unknown as { Xrm: unknown }).Xrm = fakeXrm;
  return fakeXrm;
}

function removeMockXrm(): void {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
}

describe('navItemRepository.renameNavItem', () => {
  const originalXrm = (window as unknown as { Xrm?: unknown }).Xrm;

  afterEach(() => {
    if (originalXrm) {
      (window as unknown as { Xrm: unknown }).Xrm = originalXrm;
    } else {
      removeMockXrm();
    }
    jest.clearAllMocks();
  });

  it('renameNavItem_ValidId_CallsUpdateRecordWithOnlySprkDisplayname', async () => {
    const fakeXrm = installMockXrm();

    await renameNavItem(NAV_ITEM_ID, 'My Renamed Bookmark');

    expect(fakeXrm.WebApi.updateRecord).toHaveBeenCalledTimes(1);
    expect(fakeXrm.WebApi.updateRecord).toHaveBeenCalledWith(NAVITEM_ENTITY, NAV_ITEM_ID, {
      sprk_displayname: 'My Renamed Bookmark',
    });
  });

  it('renameNavItem_WebApiThrows_WrapsInNavItemRepositoryError', async () => {
    installMockXrm({ updateRecord: jest.fn(async () => { throw new Error('privilege denied'); }) });

    await expect(renameNavItem(NAV_ITEM_ID, 'New Name')).rejects.toThrow(NavItemRepositoryError);
  });

  it('renameNavItem_NoXrmAvailable_ThrowsNavItemRepositoryError', async () => {
    removeMockXrm();

    await expect(renameNavItem(NAV_ITEM_ID, 'New Name')).rejects.toThrow(NavItemRepositoryError);
  });
});
