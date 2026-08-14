/**
 * BookmarksTab Component Tests
 * (spaarke-side-pane-navigation-history-r1 task 050/051, spec FR-07/FR-08;
 * REPLACES `PinnedTab.test.tsx` — UAT-driven redesign renamed the tab
 * "Pinned" -> "Bookmarks", moved the "Pin this page"/"+ Add bookmark"
 * gestures to the TOP of the tab, merged the former Records + Bookmarks
 * `<section>`s into ONE flat list with no section headers, moved the
 * Monitored group OUT to its own tab (`MonitoredTab.test.tsx`), and removed
 * the on-row type-chip `Badge` pill in favor of a far-left `rowIconFor`
 * icon.)
 *
 * Verifies the closed acceptance-criteria set:
 *   - The flat list renders BOTH star-pinned records and bookmarked
 *     views/links, each with a far-left icon (replaces the removed chip).
 *   - Unstarring a row calls `Xrm.WebApi.deleteRecord('sprk_navitem', ...)`
 *     and removes the row from the list.
 *   - Renders correctly in light AND dark themes (ADR-021).
 *   - No-Xrm / no-owner-id degrades to a safe empty state, never throws.
 *   - HARD assertion: `sprk_monitor` is never READ OR WRITTEN by this tab —
 *     the Monitored lens moved entirely to `MonitoredTab.tsx`.
 *   - The gesture area ("Pin this page" + bookmark input) renders ABOVE the
 *     list.
 *
 * `window.Xrm` is installed directly (mirrors `RecentTab.test.tsx`) so
 * `getXrm()` resolves it via the normal frame-walk — no module mock of
 * `navItemRepository`/`pinService`; the fake `Xrm.WebApi` drives the real
 * `listPinItems`/`unpinById` code paths.
 *
 * @see ../BookmarksTab.tsx
 * @see ../../services/pinService.ts
 * @see ADR-021 Fluent UI v9 design system (tokens, light/dark)
 * @see ADR-022 React 16/17-safe shared-lib code (navItemRepository/xrmContext this tab consumes)
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import { BookmarksTab } from '../BookmarksTab';

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

const PIN_ROWS = [
  {
    sprk_navitemid: 'pin-matter',
    sprk_type: NavItemType.Pin,
    sprk_source: 100000001,
    sprk_targetlogicalname: 'sprk_matter',
    sprk_targetid: MATTER_ID,
    sprk_pagetype: NavItemPageType.EntityRecord,
    sprk_url: null,
    sprk_displayname: 'Acme v. Widget Co',
    sprk_lastvisited: '2026-08-13T04:00:00.000Z',
    sprk_visitcount: 1,
  },
  {
    sprk_navitemid: 'pin-document',
    sprk_type: NavItemType.Pin,
    sprk_source: 100000001,
    sprk_targetlogicalname: 'sprk_document',
    sprk_targetid: DOCUMENT_ID,
    sprk_pagetype: NavItemPageType.EntityRecord,
    sprk_url: null,
    sprk_displayname: 'Master Services Agreement.docx',
    sprk_lastvisited: '2026-08-13T03:00:00.000Z',
    sprk_visitcount: 1,
  },
  {
    sprk_navitemid: 'pin-weblink',
    sprk_type: NavItemType.Pin,
    sprk_source: 100000001,
    sprk_targetlogicalname: null,
    sprk_targetid: null,
    sprk_pagetype: NavItemPageType.WebLink,
    sprk_url: 'https://example.com/reference',
    sprk_displayname: 'External Reference',
    sprk_lastvisited: '2026-08-13T02:00:00.000Z',
    sprk_visitcount: 1,
  },
];

// ─────────────────────────────────────────────────────────────────────────────
// Fake Xrm
// ─────────────────────────────────────────────────────────────────────────────

interface FakeXrmOptions {
  pinRows?: typeof PIN_ROWS;
  noUtility?: boolean;
  inaccessibleTargetIds?: string[];
  transientTargetIds?: string[];
}

function buildFakeXrm(options: FakeXrmOptions = {}) {
  const pinRows = options.pinRows ?? PIN_ROWS;
  const inaccessibleTargetIds = new Set(options.inaccessibleTargetIds ?? []);
  const transientTargetIds = new Set(options.transientTargetIds ?? []);

  const retrieveMultipleRecords = jest.fn(async (entity: string, query?: string) => {
    if (entity === 'sprk_navitem') {
      if (query?.includes(`sprk_type eq ${NavItemType.Pin}`)) {
        return { entities: pinRows };
      }
      return { entities: [] };
    }
    return { entities: [] };
  });

  const deleteRecord = jest.fn(async (_entity: string, _id: string) => ({}));
  const navigateTo = jest.fn(async () => undefined);

  const retrieveRecord = jest.fn(async (_entity: string, id: string) => {
    if (inaccessibleTargetIds.has(id)) {
      throw new Error('Insufficient privileges to access this record (403)');
    }
    if (transientTargetIds.has(id)) {
      throw new Error('Network error: failed to fetch');
    }
    return { sprk_name: 'ok' };
  });

  const xrm: Record<string, unknown> = {
    WebApi: {
      retrieveMultipleRecords,
      retrieveRecord,
      createRecord: jest.fn(),
      updateRecord: jest.fn(),
      deleteRecord,
    },
    Navigation: { navigateTo, openUrl: jest.fn(), openForm: jest.fn() },
  };

  if (!options.noUtility) {
    xrm.Utility = {
      getGlobalContext: jest.fn(() => ({
        userSettings: { userId: CURRENT_USER_ID, userName: 'Test User', languageId: 1033 },
        getClientUrl: () => 'https://spaarkedev1.crm.dynamics.com',
        getCurrentAppUrl: () => 'https://spaarkedev1.crm.dynamics.com',
        getVersion: () => '9.2',
      })),
    };
  }

  return xrm as {
    WebApi: {
      retrieveMultipleRecords: jest.Mock;
      retrieveRecord: jest.Mock;
      createRecord: jest.Mock;
      updateRecord: jest.Mock;
      deleteRecord: jest.Mock;
    };
    Navigation: { navigateTo: jest.Mock; openUrl: jest.Mock; openForm: jest.Mock };
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

function renderBookmarksTab(theme: 'light' | 'dark' = 'light') {
  return render(
    <FluentProvider theme={theme === 'light' ? webLightTheme : webDarkTheme}>
      <BookmarksTab />
    </FluentProvider>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Suite
// ─────────────────────────────────────────────────────────────────────────────

describe('BookmarksTab', () => {
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
  // Flat list — records AND bookmarks, no section headers
  // ───────────────────────────────────────────────────────────────────────

  it('render_PinnedRowsAndWeblinkBookmark_ShowInOneFlatListWithIconsAndFilledStars', async () => {
    installMockXrm();
    renderBookmarksTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('bookmarks-tab')).toBeInTheDocument();
    });

    // No section headers/groups anymore (former Records/Bookmarks split).
    expect(screen.queryByTestId('pinned-tab-group-records')).not.toBeInTheDocument();
    expect(screen.queryByTestId('pinned-tab-group-bookmarks')).not.toBeInTheDocument();

    expect(screen.getByTestId('bookmarks-tab-row-pin-matter')).toHaveTextContent('Acme v. Widget Co');
    expect(screen.getByTestId('bookmarks-tab-row-icon-pin-matter')).toBeInTheDocument();
    expect(screen.getByTestId('bookmarks-tab-row-star-pin-matter')).toHaveAttribute('aria-pressed', 'true');

    // A weblink bookmark renders in the SAME flat list, same row template.
    expect(screen.getByTestId('bookmarks-tab-row-pin-weblink')).toHaveTextContent('External Reference');
    expect(screen.getByTestId('bookmarks-tab-row-icon-pin-weblink')).toBeInTheDocument();

    // No Badge chip anywhere anymore.
    expect(screen.queryByTestId('pinned-tab-row-chip-pin-matter')).not.toBeInTheDocument();
  });

  it('render_DarkTheme_RendersAllRowsWithoutError', async () => {
    installMockXrm();
    expect(() => renderBookmarksTab('dark')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('bookmarks-tab-row-pin-matter')).toBeInTheDocument();
    });
    expect(screen.getByTestId('bookmarks-tab-row-pin-document')).toBeInTheDocument();
  });

  // ───────────────────────────────────────────────────────────────────────
  // Gesture area renders ABOVE the list (UAT redesign — moved to the top)
  // ───────────────────────────────────────────────────────────────────────

  it('render_GestureArea_RendersAboveTheList', async () => {
    installMockXrm();
    renderBookmarksTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('bookmarks-tab')).toBeInTheDocument();
    });

    const container = screen.getByTestId('bookmarks-tab-container');
    const pinButton = screen.getByTestId('bookmarks-tab-pin-current-page');
    const list = screen.getByTestId('bookmarks-tab');

    // DOM order: the gesture button precedes the list within the container.
    const position = pinButton.compareDocumentPosition(list);
    expect(container).toContainElement(pinButton);
    expect(container).toContainElement(list);
    // eslint-disable-next-line no-bitwise
    expect(position & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  // ───────────────────────────────────────────────────────────────────────
  // Unstar removes the row (DELETE on sprk_navitem)
  // ───────────────────────────────────────────────────────────────────────

  it('click_UnpinStar_DeletesSprkNavitemAndRemovesRowFromList', async () => {
    const fakeXrm = installMockXrm();
    renderBookmarksTab('light');
    const user = userEvent.setup();

    await waitFor(() => expect(screen.getByTestId('bookmarks-tab-row-star-pin-matter')).toBeInTheDocument());

    await user.click(screen.getByTestId('bookmarks-tab-row-star-pin-matter'));

    await waitFor(() => {
      expect(fakeXrm.WebApi.deleteRecord).toHaveBeenCalledWith('sprk_navitem', 'pin-matter');
    });

    await waitFor(() => {
      expect(screen.queryByTestId('bookmarks-tab-row-pin-matter')).not.toBeInTheDocument();
    });
    // The other rows are untouched.
    expect(screen.getByTestId('bookmarks-tab-row-pin-document')).toBeInTheDocument();

    // Clicking the star must not also trigger row-click navigation.
    expect(fakeXrm.Navigation.navigateTo).not.toHaveBeenCalled();
  });

  // ───────────────────────────────────────────────────────────────────────
  // Inline rename — hover pencil -> edit -> save (UAT fix #4)
  // ───────────────────────────────────────────────────────────────────────

  describe('Inline rename (UAT fix #4)', () => {
    it('click_PencilThenConfirm_CallsUpdateRecordAndShowsTheNewNameInline', async () => {
      const fakeXrm = installMockXrm();
      renderBookmarksTab('light');
      const user = userEvent.setup();

      await waitFor(() => expect(screen.getByTestId('bookmarks-tab-row-rename-pin-matter')).toBeInTheDocument());

      await user.click(screen.getByTestId('bookmarks-tab-row-rename-pin-matter'));

      const input = await screen.findByTestId('bookmarks-tab-row-rename-input-pin-matter');
      expect(input).toHaveValue('Acme v. Widget Co');

      await user.clear(input);
      await user.type(input, 'Acme Renamed');
      await user.click(screen.getByTestId('bookmarks-tab-row-rename-confirm-pin-matter'));

      // Optimistic + confirmed: the row shows the new name immediately.
      await waitFor(() => {
        expect(screen.getByTestId('bookmarks-tab-row-pin-matter')).toHaveTextContent('Acme Renamed');
      });
      expect(fakeXrm.WebApi.updateRecord).toHaveBeenCalledWith('sprk_navitem', 'pin-matter', {
        sprk_displayname: 'Acme Renamed',
      });

      // Clicking the pencil/confirming a rename must not also trigger row-click navigation.
      expect(fakeXrm.Navigation.navigateTo).not.toHaveBeenCalled();
    });

    it('keydown_EnterInRenameInput_AlsoConfirmsTheRename', async () => {
      const fakeXrm = installMockXrm();
      renderBookmarksTab('light');
      const user = userEvent.setup();

      await waitFor(() => expect(screen.getByTestId('bookmarks-tab-row-rename-pin-document')).toBeInTheDocument());
      await user.click(screen.getByTestId('bookmarks-tab-row-rename-pin-document'));

      const input = await screen.findByTestId('bookmarks-tab-row-rename-input-pin-document');
      await user.clear(input);
      await user.type(input, 'MSA (final){Enter}');

      await waitFor(() => {
        expect(fakeXrm.WebApi.updateRecord).toHaveBeenCalledWith('sprk_navitem', 'pin-document', {
          sprk_displayname: 'MSA (final)',
        });
      });
      expect(screen.queryByTestId('bookmarks-tab-row-rename-input-pin-document')).not.toBeInTheDocument();
    });

    it('keydown_EscapeInRenameInput_CancelsWithoutCallingUpdateRecord', async () => {
      const fakeXrm = installMockXrm();
      renderBookmarksTab('light');
      const user = userEvent.setup();

      await waitFor(() => expect(screen.getByTestId('bookmarks-tab-row-rename-pin-matter')).toBeInTheDocument());
      await user.click(screen.getByTestId('bookmarks-tab-row-rename-pin-matter'));

      const input = await screen.findByTestId('bookmarks-tab-row-rename-input-pin-matter');
      await user.clear(input);
      await user.type(input, 'Should not save{Escape}');

      expect(screen.queryByTestId('bookmarks-tab-row-rename-input-pin-matter')).not.toBeInTheDocument();
      expect(screen.getByTestId('bookmarks-tab-row-pin-matter')).toHaveTextContent('Acme v. Widget Co');
      expect(fakeXrm.WebApi.updateRecord).not.toHaveBeenCalled();
    });

    it('click_CancelButton_ExitsEditModeWithoutSaving', async () => {
      const fakeXrm = installMockXrm();
      renderBookmarksTab('light');
      const user = userEvent.setup();

      await waitFor(() => expect(screen.getByTestId('bookmarks-tab-row-rename-pin-weblink')).toBeInTheDocument());
      await user.click(screen.getByTestId('bookmarks-tab-row-rename-pin-weblink'));

      await screen.findByTestId('bookmarks-tab-row-rename-input-pin-weblink');
      await user.click(screen.getByTestId('bookmarks-tab-row-rename-cancel-pin-weblink'));

      expect(screen.queryByTestId('bookmarks-tab-row-rename-input-pin-weblink')).not.toBeInTheDocument();
      expect(screen.getByTestId('bookmarks-tab-row-pin-weblink')).toHaveTextContent('External Reference');
      expect(fakeXrm.WebApi.updateRecord).not.toHaveBeenCalled();
    });

    it('confirmRename_UpdateRecordFails_RevertsToTheOldNameNonFatally', async () => {
      const fakeXrm = installMockXrm();
      fakeXrm.WebApi.updateRecord.mockRejectedValueOnce(new Error('network error'));
      renderBookmarksTab('light');
      const user = userEvent.setup();

      await waitFor(() => expect(screen.getByTestId('bookmarks-tab-row-rename-pin-matter')).toBeInTheDocument());
      await user.click(screen.getByTestId('bookmarks-tab-row-rename-pin-matter'));

      const input = await screen.findByTestId('bookmarks-tab-row-rename-input-pin-matter');
      await user.clear(input);
      await user.type(input, 'Will fail to save{Enter}');

      await waitFor(() => {
        expect(screen.getByTestId('bookmarks-tab-row-pin-matter')).toHaveTextContent('Acme v. Widget Co');
      });
    });
  });

  // ───────────────────────────────────────────────────────────────────────
  // HARD MUST-NOT: sprk_monitor is never read or written by this tab
  // (the Monitored lens moved entirely to MonitoredTab.tsx)
  // ───────────────────────────────────────────────────────────────────────

  it('unpinGesture_NeverReadsOrWritesSprkMonitor', async () => {
    const fakeXrm = installMockXrm();
    renderBookmarksTab('light');
    const user = userEvent.setup();

    await waitFor(() => expect(screen.getByTestId('bookmarks-tab-row-star-pin-matter')).toBeInTheDocument());
    await user.click(screen.getByTestId('bookmarks-tab-row-star-pin-matter'));

    await waitFor(() => expect(fakeXrm.WebApi.deleteRecord).toHaveBeenCalled());

    const writtenEntities = [
      ...fakeXrm.WebApi.createRecord.mock.calls,
      ...fakeXrm.WebApi.updateRecord.mock.calls,
      ...fakeXrm.WebApi.deleteRecord.mock.calls,
    ].map(call => call[0] as string);

    expect(writtenEntities).not.toContain('sprk_monitor');
    expect(writtenEntities.every(entity => entity === 'sprk_navitem')).toBe(true);

    const readEntities = fakeXrm.WebApi.retrieveMultipleRecords.mock.calls.map(call => call[0] as string);
    expect(readEntities).not.toContain('sprk_monitor');
    // This tab never queries the Monitored entity set at all — that lens
    // moved entirely to MonitoredTab.tsx.
    expect(readEntities.every(entity => entity === 'sprk_navitem')).toBe(true);
  });

  // ───────────────────────────────────────────────────────────────────────
  // Read-time security trimming (task 080, spec FR-12/NFR-04)
  // ───────────────────────────────────────────────────────────────────────

  describe('Read-time security trimming (task 080)', () => {
    it('a denied (403) pinned record is hidden and its cached name never renders', async () => {
      installMockXrm({ inaccessibleTargetIds: [DOCUMENT_ID] });
      expect(() => renderBookmarksTab('light')).not.toThrow();

      await waitFor(() => {
        expect(screen.getByTestId('bookmarks-tab-row-pin-matter')).toBeInTheDocument();
      });

      expect(screen.queryByTestId('bookmarks-tab-row-pin-document')).not.toBeInTheDocument();
      expect(screen.queryByText('Master Services Agreement.docx')).not.toBeInTheDocument();
    });

    it('a denied (404) pinned record is trimmed in dark theme too (ADR-021)', async () => {
      installMockXrm({ inaccessibleTargetIds: [DOCUMENT_ID] });
      expect(() => renderBookmarksTab('dark')).not.toThrow();

      await waitFor(() => {
        expect(screen.getByTestId('bookmarks-tab-row-pin-matter')).toBeInTheDocument();
      });
      expect(screen.queryByTestId('bookmarks-tab-row-pin-document')).not.toBeInTheDocument();
    });

    it('a transient (network) error on the re-check does not permanently drop the row', async () => {
      installMockXrm({ transientTargetIds: [DOCUMENT_ID] });
      renderBookmarksTab('light');

      await waitFor(() => {
        expect(screen.getByTestId('bookmarks-tab-row-pin-matter')).toBeInTheDocument();
      });
      expect(screen.getByTestId('bookmarks-tab-row-pin-document')).toBeInTheDocument();
    });
  });

  // ───────────────────────────────────────────────────────────────────────
  // Empty / no-Xrm / no-owner degradation
  // ───────────────────────────────────────────────────────────────────────

  it('render_NoPins_ShowsEmptyState', async () => {
    installMockXrm({ pinRows: [] });
    renderBookmarksTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('bookmarks-tab-empty')).toHaveTextContent(
        'Pinned records, views, and links will appear here.'
      );
    });
  });

  it('mount_NoUtilityOnXrm_DegradesToEmptyStateWithoutThrowing', async () => {
    installMockXrm({ noUtility: true });
    expect(() => renderBookmarksTab('light')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('bookmarks-tab-empty')).toBeInTheDocument();
    });
  });

  it('mount_NoXrmAvailable_RendersEmptyStateWithoutThrowing', async () => {
    removeMockXrm();
    expect(() => renderBookmarksTab('light')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('bookmarks-tab-empty')).toBeInTheDocument();
    });
  });
});
