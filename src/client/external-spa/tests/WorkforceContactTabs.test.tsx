/**
 * R4 task 040 (FR-24, #2508): the record tabs for a workforce contact.
 *
 * The five record tabs (Projects, Matters, Work Assignments, Documents, Invoices) are gated on the `assigned-work` code
 * `/me/entitlements` reports. The server reports it to every CIAM partner (unchanged, R2 Option B) and, on the workforce
 * plane, only to a CONTACT that can read at least one record (`ModuleEntitlementResolver.IsGrantedWorkforceContact`,
 * tested server-side). A licensed systemuser never receives it, so on this side it reads exactly like a contact with no
 * grants. Owner 2026-11-09: a contact with no grants sees none of the tabs, not empty ones.
 *
 * What runs for real: `WorkspaceHomePage`, `PortalWorkspaceShell`, the tab hook and the widget registry's definitions and
 * entitlement rule. What is substituted, as in PortalChrome.test.tsx: the `/me` call (`fetchMeEntitlements`) and the widget
 * bodies (`getWidgetLazyComponent` → a stub), so no widget reads the BFF. The lists' record scoping is server-side and is
 * not this file's concern.
 */
import * as React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const { fetchMeEntitlements } = vi.hoisted(() => ({ fetchMeEntitlements: vi.fn() }));

vi.mock('../src/api/me-client', async () => {
  const actual = await vi.importActual<typeof import('../src/api/me-client')>('../src/api/me-client');
  return { ...actual, fetchMeEntitlements };
});

vi.mock('../src/registry/widgetRegistry', async () => {
  const actual = await vi.importActual<typeof import('../src/registry/widgetRegistry')>(
    '../src/registry/widgetRegistry'
  );
  const Stub: React.FC<{ title: string }> = ({ title }) => <div data-testid="widget-body">{title}</div>;
  return { ...actual, getWidgetLazyComponent: () => Stub };
});

import type { MeEntitlementsResponse } from '../src/api/me-client';
import { WorkspaceHomePage } from '../src/pages/WorkspaceHomePage';
import {
  RECORD_TABS_ENTITLEMENT,
  defaultWidgetIdsFor,
  isEntitledToWidgetId,
  libraryWidgetsFor,
} from '../src/registry/widgetRegistry';

afterEach(() => {
  cleanup();
  fetchMeEntitlements.mockReset();
});

const RECORD_TAB_IDS = ['projects', 'matters', 'work-assignments', 'documents', 'invoices'];
const RECORD_TAB_TITLES = ['Projects', 'Matters', 'Work Assignments', 'Documents', 'Invoices'];

/** A workforce caller without the record-tab code: a contact with no grants, or any licensed systemuser. */
const workforceWithoutRecordTabs: MeEntitlementsResponse = {
  displayName: '',
  email: '',
  plane: 'workforce',
  entitlements: ['legal-front-door', 'policy-library'],
};

/** A workforce contact the server found at least one readable record for. */
const grantedWorkforceContact: MeEntitlementsResponse = {
  ...workforceWithoutRecordTabs,
  entitlements: ['assigned-work', 'legal-front-door', 'policy-library'],
};

/** A CIAM partner, switch off: exactly what the server reports today. */
const partner: MeEntitlementsResponse = { displayName: '', email: '', plane: 'ciam', entitlements: ['assigned-work'] };

const ids = (me: MeEntitlementsResponse): string[] => libraryWidgetsFor(me).map(w => w.id);

describe('record tabs on the workforce plane (FR-24)', () => {
  it('RecordTabsEntitlement_IsTheCodeTheServerReports', () => {
    expect(RECORD_TABS_ENTITLEMENT).toBe('assigned-work');
  });

  it('GrantedWorkforceContact_GetsTheFiveRecordTabs_AsDefaultsAndInTheLibrary', () => {
    for (const id of RECORD_TAB_IDS) {
      expect(isEntitledToWidgetId(grantedWorkforceContact, id)).toBe(true);
    }
    expect(defaultWidgetIdsFor(grantedWorkforceContact)).toEqual(
      expect.arrayContaining([...RECORD_TAB_IDS, 'policy-library', 'service-requests'])
    );
    expect(ids(grantedWorkforceContact)).toEqual(expect.arrayContaining(RECORD_TAB_IDS));
  });

  it('GrantedWorkforceContact_DoesNotGetThePartnerOnlyBillingGuidelines', () => {
    expect(isEntitledToWidgetId(grantedWorkforceContact, 'billing-guidelines')).toBe(false);
  });

  it('WorkforceWithoutTheCode_GetsNoRecordTab_NotAsDefaultNotInTheLibrary', () => {
    for (const id of RECORD_TAB_IDS) {
      expect(isEntitledToWidgetId(workforceWithoutRecordTabs, id)).toBe(false);
    }
    for (const id of RECORD_TAB_IDS) {
      expect(defaultWidgetIdsFor(workforceWithoutRecordTabs)).not.toContain(id);
      expect(ids(workforceWithoutRecordTabs)).not.toContain(id);
    }
  });

  it('WorkforceWithoutTheCode_KeepsTodaysTabs', () => {
    // A licensed systemuser (or an ungranted contact): the defaults it had before this task, and a library that differs
    // from a granted contact's only by the five record tabs.
    expect(defaultWidgetIdsFor(workforceWithoutRecordTabs)).toEqual(['policy-library', 'service-requests']);
    expect(ids(workforceWithoutRecordTabs)).toEqual(
      ids(grantedWorkforceContact).filter(id => !RECORD_TAB_IDS.includes(id))
    );
  });
});

describe('record tabs for a CIAM partner (unchanged)', () => {
  it('Partner_DefaultsAndLibrary_AreExactlyTheSameAsBefore', () => {
    expect(defaultWidgetIdsFor(partner)).toEqual(RECORD_TAB_IDS);
    expect(ids(partner)).toEqual([...RECORD_TAB_IDS, 'billing-guidelines']);
  });

  it('Partner_SwitchOn_StillGetsTheRecordTabsAndServiceRequests', () => {
    const partnerSwitchOn: MeEntitlementsResponse = { ...partner, entitlements: ['assigned-work', 'legal-front-door'] };

    expect(defaultWidgetIdsFor(partnerSwitchOn)).toEqual(['service-requests', ...RECORD_TAB_IDS]);
  });
});

describe('the workspace tab strip', () => {
  const wrap = (node: React.ReactNode) => render(<FluentProvider theme={webLightTheme}>{node}</FluentProvider>);

  async function renderHome(me: MeEntitlementsResponse) {
    fetchMeEntitlements.mockResolvedValue(me);
    wrap(<WorkspaceHomePage />);
    return screen.findByRole('tablist');
  }

  it('WorkspaceHome_GrantedWorkforceContact_ShowsTheFiveRecordTabs', async () => {
    const tablist = await renderHome(grantedWorkforceContact);

    // The default tabs open in an effect once /me resolves: wait for them.
    for (const title of RECORD_TAB_TITLES) {
      expect(await within(tablist).findByText(title)).toBeTruthy();
    }
    expect(within(tablist).getByText('Quick Start')).toBeTruthy();
  });

  it('WorkspaceHome_WorkforceWithoutTheCode_ShowsNoRecordTab', async () => {
    const tablist = await renderHome(workforceWithoutRecordTabs);

    // Wait until the defaults have opened, so the absence below is not just "not yet".
    expect(await within(tablist).findByText('Policy Library')).toBeTruthy();
    expect(within(tablist).getByText('Service Requests')).toBeTruthy();
    for (const title of RECORD_TAB_TITLES) {
      expect(within(tablist).queryByText(title)).toBeNull();
    }
  });
});
