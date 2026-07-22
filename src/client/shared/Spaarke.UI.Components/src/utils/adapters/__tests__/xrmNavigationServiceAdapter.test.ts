/**
 * xrmNavigationServiceAdapter — openRecordModal contract tests.
 *
 * Locks the modal-decision standard's Layout 1 shape for opening a record as a
 * MODAL (not navigate-away): `Xrm.Navigation.navigateTo({ pageType: "entityrecord",
 * entityName, entityId }, { target: 2, position: 1, width/height 85% })`.
 * spaarke-notification-spine-r1 task 052 (FR-17) — the suggestion card's action
 * depends on this being a centred dialog, not an `openForm` page replacement.
 *
 * @see utils/adapters/xrmNavigationServiceAdapter.ts
 * @see docs/standards/MODAL-DECISION-CRITERIA.md (Layout 1)
 */

import { createXrmNavigationService } from '../xrmNavigationServiceAdapter';

describe('xrmNavigationServiceAdapter.openRecordModal', () => {
  const originalXrm = (window as { Xrm?: unknown }).Xrm;

  afterEach(() => {
    if (originalXrm) {
      (window as { Xrm?: unknown }).Xrm = originalXrm;
    } else {
      delete (window as { Xrm?: unknown }).Xrm;
    }
  });

  it('opens the record as a target:2 modal at 85% x 85% (Layout 1), not navigate-away', async () => {
    const navigateTo = jest.fn().mockResolvedValue(undefined);
    const openForm = jest.fn().mockResolvedValue(undefined);
    // getXrm() only returns the host when `.WebApi` is present (SDK-ready probe).
    (window as { Xrm?: unknown }).Xrm = { WebApi: {}, Navigation: { navigateTo, openForm } };

    const nav = createXrmNavigationService();
    await nav.openRecordModal!('sprk_matter', 'matter-123');

    // openForm (the navigate-away path) must NOT be used.
    expect(openForm).not.toHaveBeenCalled();
    expect(navigateTo).toHaveBeenCalledTimes(1);

    const [pageInput, navOptions] = navigateTo.mock.calls[0];
    expect(pageInput).toEqual({
      pageType: 'entityrecord',
      entityName: 'sprk_matter',
      entityId: 'matter-123',
    });
    expect(navOptions).toMatchObject({
      target: 2, // centred dialog — the "modal, not navigate-away" guarantee
      width: { value: 85, unit: '%' },
      height: { value: 85, unit: '%' },
    });
  });

  it('throws when the Xrm host is unavailable (non-Dataverse context)', async () => {
    delete (window as { Xrm?: unknown }).Xrm;
    const nav = createXrmNavigationService();
    await expect(nav.openRecordModal!('sprk_matter', 'matter-123')).rejects.toThrow(/Xrm\.Navigation/);
  });
});
