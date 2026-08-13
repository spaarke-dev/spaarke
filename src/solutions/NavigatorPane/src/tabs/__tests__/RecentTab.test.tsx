/**
 * RecentTab Component Tests (task 041 Viewed, task 042 adds Viewed/Edited toggle — spec FR-03/FR-04 UI)
 *
 * Verifies the task-041 closed acceptance-criteria set + `<ui-tests>`:
 *   - History rows render newest-first by `sprk_lastvisited`.
 *   - Each row's type chip matches its pagetype/logical target
 *     (Matter/Document/<other entity>/View/Page/Link).
 *   - Clicking a row invokes `Xrm.Navigation` for the row's logical target
 *     (`navigateTo` for entityrecord/entitylist, `openUrl` for weblink).
 *   - The inline star creates a per-user `sprk_type=pin` `sprk_navitem` and
 *     the row reflects the pinned state.
 *   - Renders correctly in light AND dark themes (ADR-021).
 *   - A row whose target retrieve returns 403/404 is trimmed without
 *     throwing (FR-12 minimal — full trimming is task 080).
 *
 * Plus the task-042 closed acceptance-criteria set + `<ui-tests>`:
 *   - The Viewed/Edited segmented toggle switches lists (light + dark).
 *   - Edited shows the merged `modifiedby=me` list, newest-first.
 *   - A flow-edited record (modifiedon-derived) appears in Edited.
 *   - An empty-result core-set entity contributes nothing to Edited, no error.
 *   - NO audit entity is ever queried by the Edited derivation.
 *
 * `window.Xrm` is installed directly (mirrors `NavigatorBody.test.tsx` /
 * `navigatorCaptureService.test.ts`) so `getXrm()` resolves it via its normal
 * `window.Xrm` frame-walk — no module mock of `xrmContext`,
 * `navItemRepository`, or `editedByMeService` itself; the fake `Xrm.WebApi`
 * drives real repository/service code paths (`listHistoryItems`/
 * `listPinItems`/`createPinItem`/`listEditedByMe`).
 *
 * @see ../RecentTab.tsx
 * @see ../../services/editedByMeService.ts
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

/** Five rows covering the full closed chip set, newest-first by design. */
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

/** Minimal per-entity "edited by me" row shape for the fake `retrieveMultipleRecords`. */
interface FakeEditedRecord {
  id: string;
  modifiedon: string;
  name?: string;
}

/** Primary-name field per core-set entity — mirrors the live spaarkedev1 EntityDefinitions shape (task 042 schema validation). */
const EDITED_PRIMARY_NAME_FIELD: Record<string, string> = {
  sprk_matter: 'sprk_matternumber',
  sprk_project: 'sprk_projectnumber',
  sprk_document: 'sprk_documentname',
  sprk_todo: 'sprk_name',
  sprk_event: 'sprk_eventname',
  sprk_communication: 'sprk_name',
};

interface FakeXrmOptions {
  historyRows?: typeof FIVE_TYPE_ROWS;
  pinRows?: typeof FIVE_TYPE_ROWS;
  /** Target ids that should fail `retrieveRecord` (simulates 403/404). */
  inaccessibleTargetIds?: string[];
  /** Task 042 — rows returned per core-set entity for the Edited `modifiedby=me` query. */
  editedRecordsByEntity?: Partial<Record<string, FakeEditedRecord[]>>;
}

function buildFakeXrm(options: FakeXrmOptions = {}) {
  const historyRows = options.historyRows ?? FIVE_TYPE_ROWS;
  const pinRows = options.pinRows ?? [];
  const inaccessibleTargetIds = new Set(options.inaccessibleTargetIds ?? []);
  const editedRecordsByEntity = options.editedRecordsByEntity ?? {};

  const retrieveMultipleRecords = jest.fn(async (entity: string, query?: string) => {
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
    if (query?.includes('_modifiedby_value eq')) {
      // Task 042 — Edited derivation: per-core-entity modifiedby=me query.
      const idField = `${entity}id`;
      const nameField = EDITED_PRIMARY_NAME_FIELD[entity];
      const rows = editedRecordsByEntity[entity] ?? [];
      return {
        entities: rows.map(r => ({
          [idField]: r.id,
          modifiedon: r.modifiedon,
          ...(nameField && r.name !== undefined ? { [nameField]: r.name } : {}),
        })),
      };
    }
    return { entities: [] };
  });

  const getEntityMetadata = jest.fn(async (entity: string) => ({
    PrimaryNameAttribute: EDITED_PRIMARY_NAME_FIELD[entity] ?? 'name',
  }));

  const retrieveRecord = jest.fn(async (_entity: string, id: string) => {
    if (inaccessibleTargetIds.has(id)) {
      throw new Error('Insufficient privileges to access this record (403)');
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
      // Task 042 — Edited derivation's display-name resolution.
      getEntityMetadata,
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
  // Newest-first ordering + chip mapping (light + dark)
  // ───────────────────────────────────────────────────────────────────────

  it('render_HistoryRows_ShowNewestFirstWithCorrectChips', async () => {
    installMockXrm();
    renderRecentTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('recent-tab')).toBeInTheDocument();
    });

    const rowIds = FIVE_TYPE_ROWS.map(r => r.sprk_navitemid);
    for (const id of rowIds) {
      expect(screen.getByTestId(`recent-tab-row-${id}`)).toBeInTheDocument();
    }

    // Newest-first: DOM order matches FIVE_TYPE_ROWS order (already newest -> oldest).
    const renderedIds = screen
      .getAllByRole('listitem')
      .map(el => el.getAttribute('data-testid'));
    expect(renderedIds).toEqual(rowIds.map(id => `recent-tab-row-${id}`));

    // Chip mapping — closed set (Matter/Document/View/Page/Link).
    expect(screen.getByTestId('recent-tab-row-chip-nav-matter')).toHaveTextContent('Matter');
    expect(screen.getByTestId('recent-tab-row-chip-nav-document')).toHaveTextContent('Document');
    expect(screen.getByTestId('recent-tab-row-chip-nav-view')).toHaveTextContent('View');
    expect(screen.getByTestId('recent-tab-row-chip-nav-page')).toHaveTextContent('Page');
    expect(screen.getByTestId('recent-tab-row-chip-nav-link')).toHaveTextContent('Link');
  });

  it('render_DarkTheme_RendersAllRowsWithoutError', async () => {
    installMockXrm();
    expect(() => renderRecentTab('dark')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('recent-tab')).toBeInTheDocument();
    });
    expect(screen.getByTestId('recent-tab-row-nav-matter')).toBeInTheDocument();
    expect(screen.getByTestId('recent-tab-row-chip-nav-link')).toHaveTextContent('Link');
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
  // Inaccessible row trimmed without throwing (FR-12 minimal)
  // ───────────────────────────────────────────────────────────────────────

  it('render_InaccessibleTarget_TrimsRowWithoutThrowing', async () => {
    installMockXrm({
      historyRows: [FIVE_TYPE_ROWS[0], FIVE_TYPE_ROWS[1]], // Matter (accessible) + Document (inaccessible)
      inaccessibleTargetIds: [DOCUMENT_ID],
    });

    expect(() => renderRecentTab('light')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('recent-tab-row-nav-matter')).toBeInTheDocument();
    });

    expect(screen.queryByTestId('recent-tab-row-nav-document')).not.toBeInTheDocument();
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

  // ───────────────────────────────────────────────────────────────────────
  // Task 042 — Viewed/Edited segmented toggle (spec FR-04 / OQ-5)
  // ───────────────────────────────────────────────────────────────────────

  describe('Viewed/Edited toggle (task 042)', () => {
    const EDITED_FIXTURES = {
      sprk_matter: [{ id: 'edited-matter-1', modifiedon: '2026-08-10T10:00:00.000Z', name: 'MTR-100' }],
      sprk_document: [{ id: 'edited-doc-1', modifiedon: '2026-08-12T09:00:00.000Z', name: 'Amendment.docx' }],
      // sprk_project, sprk_todo, sprk_event, sprk_communication intentionally
      // omitted — empty-result entities, exercised by the negative case below.
    };

    it('toggle_SwitchToEdited_ShowsMergedModifiedByMeListSortedDesc (light)', async () => {
      const fakeXrm = installMockXrm({ editedRecordsByEntity: EDITED_FIXTURES });
      renderRecentTab('light');
      const user = userEvent.setup();

      // Default mode is Viewed — verify it first.
      await waitFor(() => expect(screen.getByTestId('recent-tab')).toBeInTheDocument());
      expect(screen.getByTestId('recent-tab-mode-viewed')).toHaveAttribute('aria-pressed', 'true');
      expect(screen.getByTestId('recent-tab-mode-edited')).toHaveAttribute('aria-pressed', 'false');

      await user.click(screen.getByTestId('recent-tab-mode-edited'));

      await waitFor(() => {
        expect(screen.getByTestId('recent-tab-edited')).toBeInTheDocument();
      });

      // Newest-first: document (2026-08-12) before matter (2026-08-10).
      const renderedIds = screen
        .getAllByRole('listitem')
        .map(el => el.getAttribute('data-testid'));
      expect(renderedIds).toEqual([
        'recent-tab-edited-row-sprk_document-edited-doc-1',
        'recent-tab-edited-row-sprk_matter-edited-matter-1',
      ]);
      expect(screen.getByTestId('recent-tab-edited-row-sprk_document-edited-doc-1')).toHaveTextContent(
        'Amendment.docx'
      );
      expect(
        screen.getByTestId('recent-tab-edited-row-chip-sprk_document-edited-doc-1')
      ).toHaveTextContent('Document');

      // Viewed's own rows are no longer rendered while in Edited mode.
      expect(screen.queryByTestId('recent-tab')).not.toBeInTheDocument();

      // Every core-set entity was queried, none of them 'audit'.
      const queriedEntities = fakeXrm.WebApi.retrieveMultipleRecords.mock.calls.map(call => call[0]);
      expect(queriedEntities).not.toContain('audit');
      expect(queriedEntities).toEqual(
        expect.arrayContaining([
          'sprk_matter',
          'sprk_project',
          'sprk_document',
          'sprk_todo',
          'sprk_event',
          'sprk_communication',
        ])
      );
    });

    it('toggle_SwitchToEdited_RendersWithoutErrorInDarkTheme', async () => {
      installMockXrm({ editedRecordsByEntity: EDITED_FIXTURES });
      expect(() => renderRecentTab('dark')).not.toThrow();
      const user = userEvent.setup();

      await waitFor(() => expect(screen.getByTestId('recent-tab-mode-edited')).toBeInTheDocument());
      await user.click(screen.getByTestId('recent-tab-mode-edited'));

      await waitFor(() => {
        expect(screen.getByTestId('recent-tab-edited')).toBeInTheDocument();
      });
      expect(screen.getByTestId('recent-tab-edited-row-sprk_document-edited-doc-1')).toBeInTheDocument();
    });

    it('click_ViewedAfterEdited_RestoresTheOriginalHistoryList', async () => {
      installMockXrm({ editedRecordsByEntity: EDITED_FIXTURES });
      renderRecentTab('light');
      const user = userEvent.setup();

      await waitFor(() => expect(screen.getByTestId('recent-tab')).toBeInTheDocument());
      await user.click(screen.getByTestId('recent-tab-mode-edited'));
      await waitFor(() => expect(screen.getByTestId('recent-tab-edited')).toBeInTheDocument());

      await user.click(screen.getByTestId('recent-tab-mode-viewed'));

      await waitFor(() => {
        expect(screen.getByTestId('recent-tab')).toBeInTheDocument();
      });
      expect(screen.getByTestId('recent-tab-row-nav-matter')).toBeInTheDocument();
      expect(screen.queryByTestId('recent-tab-edited')).not.toBeInTheDocument();
    });

    it('toggle_FlowEditedRecordAndEmptyResultEntity_AppearsAndIsHarmless', async () => {
      // sprk_event was edited by a flow (not the UI) — no capture/history
      // row for it exists anywhere in this fixture; it only shows up because
      // its own modifiedon-derived query returns it. sprk_project,
      // sprk_todo, sprk_communication are intentionally left with zero rows
      // (empty-result entities) — they must contribute nothing and must not
      // cause an error.
      installMockXrm({
        historyRows: [],
        editedRecordsByEntity: {
          sprk_event: [{ id: 'flow-event-1', modifiedon: '2026-08-13T03:00:00.000Z', name: 'Auto-scheduled hearing' }],
        },
      });
      renderRecentTab('light');
      const user = userEvent.setup();

      await waitFor(() => expect(screen.getByTestId('recent-tab-mode-edited')).toBeInTheDocument());
      await user.click(screen.getByTestId('recent-tab-mode-edited'));

      await waitFor(() => {
        expect(screen.getByTestId('recent-tab-edited')).toBeInTheDocument();
      });

      expect(screen.getByTestId('recent-tab-edited-row-sprk_event-flow-event-1')).toHaveTextContent(
        'Auto-scheduled hearing'
      );
      // Exactly one row rendered — the five empty-result entities contributed nothing.
      expect(screen.getAllByRole('listitem')).toHaveLength(1);
    });
  });
});
