/**
 * Portal chrome per audience (owner, task 016 retest, 2026-11-17):
 *   - the header title follows the plane: a partner reads "Outside Counsel Collaboration", an internal contact
 *     "Legal Service Request Portal"; the decorative Home/Services/Resources/Help strip and the "Welcome to …" band are gone;
 *   - a partner (CIAM) has no Quick Start tab and lands on the first default widget; an internal contact keeps Quick Start;
 *   - the assistant pane starts collapsed (the rail with "Open assistant"), because it is not activated yet.
 *
 * What runs for real: `AppHeader`, `WorkspaceHomePage`, `PortalWorkspaceShell`, the tab hook and the widget registry's
 * definitions and entitlement rule. What is substituted: the `/me` call (`fetchMeEntitlements`) and the widget bodies
 * (`getWidgetLazyComponent` → a stub that names the widget), so no widget reads the BFF.
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
import { AppHeader, PARTNER_PORTAL_TITLE, WORKFORCE_PORTAL_TITLE, portalTitleFor } from '../src/components/AppHeader';
import { WorkspaceHomePage, hasQuickStartFor } from '../src/pages/WorkspaceHomePage';
import { defaultWidgetIdsFor, getWidgetDefinition } from '../src/registry/widgetRegistry';

afterEach(() => {
  cleanup();
  fetchMeEntitlements.mockReset();
});

const partner: MeEntitlementsResponse = { displayName: '', email: '', plane: 'ciam', entitlements: ['assigned-work'] };
const workforce: MeEntitlementsResponse = {
  displayName: '',
  email: '',
  plane: 'workforce',
  entitlements: ['legal-front-door', 'policy-library'],
};

const wrap = (node: React.ReactNode) => render(<FluentProvider theme={webLightTheme}>{node}</FluentProvider>);

async function renderHome(me: MeEntitlementsResponse) {
  fetchMeEntitlements.mockResolvedValue(me);
  wrap(<WorkspaceHomePage />);
  return screen.findByRole('tablist');
}

describe('AppHeader title per plane', () => {
  it('portalTitleFor_PartnerPlane_ReadsOutsideCounselCollaboration', () => {
    expect(portalTitleFor(false)).toBe('Outside Counsel Collaboration');
    expect(PARTNER_PORTAL_TITLE).toBe('Outside Counsel Collaboration');
  });

  it('portalTitleFor_WorkforcePlane_ReadsLegalServiceRequestPortal', () => {
    expect(portalTitleFor(true)).toBe('Legal Service Request Portal');
    expect(WORKFORCE_PORTAL_TITLE).toBe('Legal Service Request Portal');
  });

  it('AppHeader_Rendered_ShowsTheTitleWithoutTheNavStripOrWelcomeBand', () => {
    wrap(<AppHeader isDark={false} title={PARTNER_PORTAL_TITLE} />);

    const banner = screen.getByRole('banner');
    expect(within(banner).getByText(PARTNER_PORTAL_TITLE)).toBeTruthy();
    expect(within(banner).queryByRole('navigation')).toBeNull();
    for (const removed of ['Home', 'Services', 'Resources', 'Help']) {
      expect(within(banner).queryByText(removed)).toBeNull();
    }
    expect(screen.queryByText(/Welcome to the/)).toBeNull();
    expect(screen.queryByText(/Legal Department Service Portal/)).toBeNull();
  });
});

describe('Quick Start per plane', () => {
  it('hasQuickStartFor_ByPlane_OnlyThePartnerHasNone', () => {
    expect(hasQuickStartFor({ plane: 'ciam' })).toBe(false);
    expect(hasQuickStartFor({ plane: 'workforce' })).toBe(true);
    expect(hasQuickStartFor({ plane: 'admin' })).toBe(true);
  });

  it('WorkspaceHome_Partner_HasNoQuickStartTabAndOpensOnTheFirstDefaultWidget', async () => {
    const tablist = await renderHome(partner);

    expect(within(tablist).queryByText('Quick Start')).toBeNull();
    const firstDefault = getWidgetDefinition(defaultWidgetIdsFor(partner)[0]);
    expect(firstDefault).toBeTruthy();
    expect((await screen.findByTestId('widget-body')).textContent).toBe(firstDefault!.title);
  });

  it('WorkspaceHome_InternalContact_KeepsQuickStartAsTheInitialTab', async () => {
    const tablist = await renderHome(workforce);

    expect(within(tablist).getByText('Quick Start')).toBeTruthy();
    expect(screen.queryByTestId('widget-body')).toBeNull();
  });
});

describe('Assistant pane default', () => {
  it('WorkspaceHome_OnLoad_ShowsTheCollapsedAssistantRail', async () => {
    await renderHome(workforce);

    expect(screen.getByRole('button', { name: 'Open assistant' })).toBeTruthy();
    expect(screen.queryByRole('complementary', { name: 'AI Assistant' })).toBeNull();
  });
});
