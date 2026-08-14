/**
 * RecentTab Component Tests
 * (spaarke-side-pane-navigation-history-r1 task 041, spec FR-03 UI; UPDATED
 * for the UAT-driven redesign — the task-042 Viewed/Edited segmented toggle
 * is REMOVED, the on-row type-chip `Badge` pill is REMOVED in favor of a
 * far-left `rowIconFor` icon.)
 *
 * Verifies the closed acceptance-criteria set:
 *   - History rows render newest-first by `sprk_lastvisited`.
 *   - Each row shows a far-left record-type icon (replaces the removed chip).
 *   - Clicking a row invokes `Xrm.Navigation` for the row's logical target
 *     (`navigateTo` for entityrecord/entitylist, `openUrl` for weblink).
 *   - The inline star creates a per-user `sprk_type=pin` `sprk_navitem` and
 *     the row reflects the pinned state.
 *   - Renders correctly in light AND dark themes (ADR-021).
 *   - A row whose target retrieve returns 403/404 is trimmed without
 *     throwing (FR-12, task 080).
 *   - There is NO Viewed/Edited toggle anywhere in this tab.
 *
 * `window.Xrm` is installed directly (mirrors `NavigatorBody.test.tsx`) so
 * `getXrm()` resolves it via its normal `window.Xrm` frame-walk — no module
 * mock of `xrmContext` or `navItemRepository` itself; the fake `Xrm.WebApi`
 * drives real repository code paths (`listHistoryItems`/`listPinItems`/
 * `createPinItem`).
 *
 * @see ../RecentTab.tsx
 * @see ADR-021 Fluent UI v9 design system (tokens, light/dark)
 * @see ADR-022 React 16/17-safe shared-lib code (navItemRepository/xrmContext this tab consumes)
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import { RecentTab } from '../RecentTab';

// ─────────────────────────────────────────────────────────────────────────────
// Fixtures
// ─────────────────────────────────────────────────────────────────────────────

const CURRENT_USER_ID = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

const NavItemType = { History: 100000000, Pin: 100000001 } as const;
const NavItemPageType = {
  EntityRecord: 100000000,
  EntityList: 100000001,
  Custom: 100000002,
  WebLink: 100000003,
} as const;

const MATTER_ID = '11111111-1111-1111-1111-111111111111';
const DOCUMENT_ID = '22222222-2222-2222-2222-222222222222';

/** Five rows covering the full closed pagetype set, newest-first by design. */
const FIVE_TYPE_ROWS = [
  {
    sprk_navitemid: 'nav-matter',
    sprk_type: NavItemType.History,
    sprk_source: 100000000,
    sprk_targetlogicalname: 'sprk_matter',
    sprk_targetid: MATTER_ID,
    sprk_pagetype: NavItemPageType.EntityRecord,
    sprk_url: null,
    sprk_displayname: 'Acme v. Widget Co',
    sprk_lastvisited: '2026-08-13T04:00:00.000Z',
    sprk_visitcount: 3,
  },
  {
    sprk_navitemid: 'nav-document',
    sprk_type: NavItemType.History,
    sprk_source: 100000000,
    sprk_targetlogicalname: 'sprk_document',
    sprk_targetid: DOCUMENT_ID,
    sprk_pagetype: NavItemPageType.EntityRecord,
    sprk_url: null,
    sprk_displayname: 'Master Services Agreement.docx',
    sprk_lastvisited: '2026-08-13T03:00:00.000Z',
    sprk_visitcount: 1,
  },
  {
    sprk_navitemid: 'nav-view',
    sprk_type: NavItemType.History,
    sprk_source: 100000000,
    sprk_targetlogicalname: 'sprk_document',
    sprk_targetid: null,
    sprk_pagetype: NavItemPageType.EntityList,
    sprk_url: null,
    sprk_displayname: 'All Documents',
    sprk_lastvisited: '2026-08-13T02:00:00.000Z',
    sprk_visitcount: 1,
  },
  {
    sprk_navitemid: 'nav-page',
    sprk_type: NavItemType.History,
    sprk_source: 100000000,
    sprk_targetlogicalname: null,
    sprk_targetid: null,
    sprk_pagetype: NavItemPageType.Custom,
    sprk_url: null,
    sprk_displayname: 'Analytics Dashboard',
    sprk_lastvisited: '2026-08-13T01:00:00.000Z',
    sprk_visitcount: 1,
  },
  {
    sprk_navitemid: 'nav-link',
    sprk_type: NavItemType.History,
    sprk_source: 100000000,
    sprk_targetlogicalname: null,
    sprk_targetid: null,
    sprk_pagetype: NavItemPageType.WebLink,
    sprk_url: 'https://example.com/reference',
    sprk_displayname: 'External Reference',
    sprk_lastvisited: '2026-08-13T00:00:00.000Z',
    sprk_visitcount: 1,
  },
];

// ─────────────────────────────────────────────────────────────────────────────
// Fake Xrm
// ─────────────────────────────────────────────────────────────────────────────

interface FakeXrmOptions {
  historyRows?: typeof FIVE_TYPE_ROWS;
  pinRows?: typeof FIVE_TYPE_ROWS;
  /** Target ids that should fail `retrieveRecord` with a 403/404-shaped error (task 080 — classifies `denied`). */
  inaccessibleTargetIds?: string[];
  /** Target ids that should fail `retrieveRecord` with a network/timeout-shaped error (task 080 — classifies `transient`, row is KEPT). */
  transientTargetIds?: string[];
}

function buildFakeXrm(options: FakeXrmOptions = {}) {
  const historyRows = options.historyRows ?? FIVE_TYPE_ROWS;
  const pinRows = options.pinRows ?? [];
  const inaccessibleTargetIds = new Set(options.inaccessibleTargetIds ?? []);
  const transientTargetIds = new Set(options.transientTargetIds ?? []);

  const retrieveMultipleRecords = jest.fn(async (_entity: string, query?: string) => {
    const isHistory = query?.includes(`sprk_type eq ${NavItemType.History}`);
    const isPin = query?.includes(`sprk_type eq ${NavItemType.Pin}`);
    if (isHistory) {
      const sorted = [...historyRows].sort(
        (a, b) => new Date(b.sprk_lastvisited).getTime() - new Date(a.sprk_lastvisited).getTime()
      );
      return { entities: sorted };
    }
    if (isPin) {
      return { entities: pinRows };
    }
    return { entities: [] };
  });

  const retrieveRecord = jest.fn(async (_entity: string, id: string) => {
    if (inaccessibleTargetIds.has(id)) {
      throw new Error('Insufficient privileges to access this record (403)');
    }
    if (transientTargetIds.has(id)) {
      throw new Error('Network error: failed to fetch');
    }
    return { sprk_name: 'ok' };
  });

  const createRecord = jest.fn(async (entity: string, data: Record<string, unknown>) => {
    return { id: 'new-pin-id', entityType: entity, ...data };
  });

  const navigateTo = jest.fn(async () => undefined);
  const openUrl = jest.fn();

  return {
    WebApi: {
      retrieveMultipleRecords,
      retrieveRecord,
      createRecord,
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    },
    Navigation: {
      navigateTo,
      openUrl,
      openForm: jest.fn(),
    },
    Utility: {
      getGlobalContext: jest.fn(() => ({
        userSettings: { userId: CURRENT_USER_ID, userName: 'Test User', languageId: 1033 },
        getClientUrl: () => 'https://spaarkedev1.crm.dynamics.com',
        getCurrentAppUrl: () => 'https://spaarkedev1.crm.dynamics.com',
        getVersion: () => '9.2',
      })),
    },
  };
}

function installMockXrm(options?: FakeXrmOptions) {
  const fakeXrm = buildFakeXrm(options);
  (window as unknown as { Xrm: unknown }).Xrm = fakeXrm;
  return fakeXrm;
}

function removeMockXrm(): void {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
}

function renderRecentTab(theme: 'light' | 'dark' = 'light') {
  return render(
    <FluentProvider theme={theme === 'light' ? webLightTheme : webDarkTheme}>
      <RecentTab />
    </FluentProvider>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Suite
// ─────────────────────────────────────────────────────────────────────────────

describe('RecentTab', () => {
  const originalXrm = (window as unknown as { Xrm?: unknown }).Xrm;

  afterEach(() => {
    if (originalXrm) {
      (window as unknown as { Xrm: unknown }).Xrm = originalXrm;
    } else {
      removeMockXrm();
    }
    jest.clearAllMocks();
  });

  // ───────────────────────────────────────────────────────────────────────
  // Newest-first ordering + far-left icon (replaces the removed chip)
  // ───────────────────────────────────────────────────────────────────────

  it('render_HistoryRows_ShowNewestFirstWithFarLeftIcons', async () => {
    installMockXrm();
    renderRecentTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('recent-tab')).toBeInTheDocument();
    });

    const rowIds = FIVE_TYPE_ROWS.map(r => r.sprk_navitemid);
    for (const id of rowIds) {
      expect(screen.getByTestId(`recent-tab-row-${id}`)).toBeInTheDocument();
      // Icon presence (replaces the removed Badge pill) — every row leads
      // with a far-left record-type icon, whatever its pagetype.
      const icon = screen.getByTestId(`recent-tab-row-icon-${id}`);
      expect(icon).toBeInTheDocument();
      expect(icon.querySelector('svg')).not.toBeNull();
    }

    // Newest-first: DOM order matches FIVE_TYPE_ROWS order (already newest -> oldest).
    const renderedIds = screen
      .getAllByRole('listitem')
      .map(el => el.getAttribute('data-testid'));
    expect(renderedIds).toEqual(rowIds.map(id => `recent-tab-row-${id}`));

    // No Badge chip anywhere anymore.
    expect(screen.queryByTestId('recent-tab-row-chip-nav-matter')).not.toBeInTheDocument();
  });

  it('render_DarkTheme_RendersAllRowsWithoutError', async () => {
    installMockXrm();
    expect(() => renderRecentTab('dark')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('recent-tab')).toBeInTheDocument();
    });
    expect(screen.getByTestId('recent-tab-row-nav-matter')).toBeInTheDocument();
    expect(screen.getByTestId('recent-tab-row-icon-nav-link')).toBeInTheDocument();
  });

  // ───────────────────────────────────────────────────────────────────────
  // No Viewed/Edited toggle anywhere (UAT redesign removed it)
  // ───────────────────────────────────────────────────────────────────────

  it('render_NeverShowsTheRemovedViewedEditedToggle', async () => {
    installMockXrm();
    renderRecentTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('recent-tab')).toBeInTheDocument();
    });
    expect(screen.queryByTestId('recent-tab-mode-toggle')).not.toBeInTheDocument();
    expect(screen.queryByTestId('recent-tab-mode-viewed')).not.toBeInTheDocument();
    expect(screen.queryByTestId('recent-tab-mode-edited')).not.toBeInTheDocument();
    expect(screen.queryByTestId('recent-tab-edited')).not.toBeInTheDocument();
  });

  // ───────────────────────────────────────────────────────────────────────
  // Click-to-navigate
  // ───────────────────────────────────────────────────────────────────────

  it('click_MatterRow_InvokesXrmNavigationForEntityRecordTarget', async () => {
    const fakeXrm = installMockXrm();
    renderRecentTab('light');
    const user = userEvent.setup();

    await waitFor(() => expect(screen.getByTestId('recent-tab-row-nav-matter')).toBeInTheDocument());

    await user.click(screen.getByTestId('recent-tab-row-nav-matter'));

    expect(fakeXrm.Navigation.navigateTo).toHaveBeenCalledWith({
      pageType: 'entityrecord',
      entityName: 'sprk_matter',
      entityId: MATTER_ID,
    });
  });

  it('click_ViewRow_InvokesXrmNavigationForEntityListTarget', async () => {
    const fakeXrm = installMockXrm();
    renderRecentTab('light');
    const user = userEvent.setup();

    await waitFor(() => expect(screen.getByTestId('recent-tab-row-nav-view')).toBeInTheDocument());
    await user.click(screen.getByTestId('recent-tab-row-nav-view'));

    expect(fakeXrm.Navigation.navigateTo).toHaveBeenCalledWith({
      pageType: 'entitylist',
      entityName: 'sprk_document',
    });
  });

  it('click_LinkRow_OpensUrlViaXrmNavigation', async () => {
    const fakeXrm = installMockXrm();
    renderRecentTab('light');
    const user = userEvent.setup();

    await waitFor(() => expect(screen.getByTestId('recent-tab-row-nav-link')).toBeInTheDocument());
    await user.click(screen.getByTestId('recent-tab-row-nav-link'));

    expect(fakeXrm.Navigation.openUrl).toHaveBeenCalledWith('https://example.com/reference');
    expect(fakeXrm.Navigation.navigateTo).not.toHaveBeenCalled();
  });

  // ───────────────────────────────────────────────────────────────────────
  // Promote-to-pin (star)
  // ───────────────────────────────────────────────────────────────────────

  it('click_Star_CreatesPinRowAndReflectsPinnedState', async () => {
    const fakeXrm = installMockXrm();
    renderRecentTab('light');
    const user = userEvent.setup();

    await waitFor(() => expect(screen.getByTestId('recent-tab-row-star-nav-matter')).toBeInTheDocument());

    const star = screen.getByTestId('recent-tab-row-star-nav-matter');
    expect(star).toHaveAttribute('aria-pressed', 'false');

    await user.click(star);

    await waitFor(() => {
      expect(fakeXrm.WebApi.createRecord).toHaveBeenCalledWith(
        'sprk_navitem',
        expect.objectContaining({
          sprk_type: NavItemType.Pin,
          sprk_targetlogicalname: 'sprk_matter',
          sprk_targetid: MATTER_ID,
          sprk_displayname: 'Acme v. Widget Co',
        })
      );
    });

    // Clicking the star must not also trigger row-click navigation.
    expect(fakeXrm.Navigation.navigateTo).not.toHaveBeenCalled();

    await waitFor(() => {
      expect(screen.getByTestId('recent-tab-row-star-nav-matter')).toHaveAttribute('aria-pressed', 'true');
    });
  });

  // ───────────────────────────────────────────────────────────────────────
  // Read-time security trimming (task 080, spec FR-12/NFR-04)
  // ───────────────────────────────────────────────────────────────────────

  it('render_InaccessibleTarget_TrimsRowWithoutThrowingAndNeverRendersCachedName', async () => {
    installMockXrm({
      historyRows: [FIVE_TYPE_ROWS[0], FIVE_TYPE_ROWS[1]], // Matter (accessible) + Document (denied)
      inaccessibleTargetIds: [DOCUMENT_ID],
    });

    expect(() => renderRecentTab('light')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('recent-tab-row-nav-matter')).toBeInTheDocument();
    });

    // Row hidden entirely — and the cached name never appears anywhere in
    // the DOM (no flash, no partial render, no "(no longer available)"
    // stand-in that could be confused with the real name).
    expect(screen.queryByTestId('recent-tab-row-nav-document')).not.toBeInTheDocument();
    expect(screen.queryByText('Master Services Agreement.docx')).not.toBeInTheDocument();
  });

  it('render_InaccessibleTarget_TrimsRowInDarkThemeToo (ADR-021)', async () => {
    installMockXrm({
      historyRows: [FIVE_TYPE_ROWS[0], FIVE_TYPE_ROWS[1]],
      inaccessibleTargetIds: [DOCUMENT_ID],
    });

    expect(() => renderRecentTab('dark')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('recent-tab-row-nav-matter')).toBeInTheDocument();
    });
    expect(screen.queryByTestId('recent-tab-row-nav-document')).not.toBeInTheDocument();
    expect(screen.queryByText('Master Services Agreement.docx')).not.toBeInTheDocument();
  });

  it('render_AccessibleTarget_ShowsNameNormally', async () => {
    installMockXrm({
      historyRows: [FIVE_TYPE_ROWS[0]],
    });
    renderRecentTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('recent-tab-row-nav-matter')).toBeInTheDocument();
    });
    expect(screen.getByText('Acme v. Widget Co')).toBeInTheDocument();
  });

  it('render_TransientErrorOnRecheck_KeepsTheRow (does not permanently drop an accessible row on a blip)', async () => {
    installMockXrm({
      historyRows: [FIVE_TYPE_ROWS[0], FIVE_TYPE_ROWS[1]], // Matter (accessible) + Document (transient failure)
      transientTargetIds: [DOCUMENT_ID],
    });

    renderRecentTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('recent-tab-row-nav-matter')).toBeInTheDocument();
    });

    // Transient (network/timeout) failure on the re-check is NOT a denial —
    // the row is kept, not trimmed.
    expect(screen.getByTestId('recent-tab-row-nav-document')).toBeInTheDocument();
    expect(screen.getByText('Master Services Agreement.docx')).toBeInTheDocument();
  });

  // ───────────────────────────────────────────────────────────────────────
  // No-Xrm / empty-state degradation
  // ───────────────────────────────────────────────────────────────────────

  it('mount_NoXrmAvailable_RendersEmptyStateWithoutThrowing', async () => {
    removeMockXrm();
    expect(() => renderRecentTab('light')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('recent-tab-empty')).toBeInTheDocument();
    });
  });
});
