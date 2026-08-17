/**
 * recordNavigation tests — the sprk_communication → Email code page routing
 * rule (spaarke-side-pane-navigation-history-r1, UAT).
 */

import { openEntityRecord, COMMUNICATION_ENTITY, EMAIL_CODE_PAGE } from '../recordNavigation';
import type { XrmContext } from '@spaarke/ui-components';

function makeXrm() {
  const navigateTo = jest.fn().mockResolvedValue(undefined);
  const xrm = { Navigation: { navigateTo } } as unknown as XrmContext;
  return { xrm, navigateTo };
}

describe('openEntityRecord', () => {
  it('routes a sprk_communication record to the Email code page in single-record mode', () => {
    const { xrm, navigateTo } = makeXrm();
    openEntityRecord(xrm, COMMUNICATION_ENTITY, '{ABC00000-0000-0000-0000-000000000001}');
    expect(navigateTo).toHaveBeenCalledTimes(1);
    expect(navigateTo).toHaveBeenCalledWith({
      pageType: 'webresource',
      webresourceName: EMAIL_CODE_PAGE,
      // GUID normalized (braces stripped, lowercased) + single-record flag folded in.
      data: 'abc00000-0000-0000-0000-000000000001&single=1',
    });
  });

  it('opens every other entity via the OOB main form (entityrecord)', () => {
    const { xrm, navigateTo } = makeXrm();
    openEntityRecord(xrm, 'sprk_matter', 'matter-1');
    expect(navigateTo).toHaveBeenCalledWith({
      pageType: 'entityrecord',
      entityName: 'sprk_matter',
      entityId: 'matter-1',
    });
  });

  it('no-ops when the id is missing (never navigates to a malformed target)', () => {
    const { xrm, navigateTo } = makeXrm();
    openEntityRecord(xrm, 'sprk_matter', null);
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('no-ops (does not throw) when Navigation is unavailable', () => {
    const xrm = {} as unknown as XrmContext;
    expect(() => openEntityRecord(xrm, COMMUNICATION_ENTITY, 'x')).not.toThrow();
  });
});
