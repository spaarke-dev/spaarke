/**
 * PinnedTab Component Tests (task 050, spec FR-07 UI; task 052 adds the
 * Monitored group coverage below)
 *
 * Verifies the task-050 closed acceptance-criteria set + `<ui-tests>`:
 *   - The Records group renders the current user's pin `sprk_navitem` rows.
 *   - Unstarring a row calls `Xrm.WebApi.deleteRecord('sprk_navitem', ...)`
 *     and removes the row from Records.
 *   - Renders correctly in light AND dark themes (ADR-021).
 *   - No-Xrm / no-owner-id degrades to a safe empty state, never throws.
 *   - HARD assertion: `sprk_monitor` is never WRITTEN by any WebApi call the
 *     Records/pin gesture triggers (the task-052 Monitored group legitimately
 *     READS `sprk_monitor` via `retrieveMultipleRecords` — see the task-052
 *     describe block below; this assertion is scoped to create/update/delete).
 *
 * Task-052 additions verify the Monitored group's closed acceptance-criteria
 * set + `<ui-tests>`:
 *   - Lists the user's OWNED, `sprk_monitor=true` records in a group visually
 *     DISTINCT from (never merged with) Records.
 *   - A `sprk_monitor=true` record NOT owned by the user is excluded.
 *   - The group surfaces shared-flag semantics copy (affects everyone,
 *     last-writer-wins).
 *   - Renders correctly in light AND dark themes (ADR-021).
 *
 * `window.Xrm` is installed directly (mirrors `RecentTab.test.tsx`) so
 * `getXrm()` resolves it via the normal frame-walk — no module mock of
 * `navItemRepository`/`pinService`/`monitoredService`; the fake `Xrm.WebApi`
 * drives the real `listPinItems`/`unpinById`/`listMonitoredByMe` code paths.
 *
 * @see ../PinnedTab.tsx
 * @see ../../services/pinService.ts
 * @see ../../services/monitoredService.ts
 * @see ADR-021 Fluent UI v9 design system (tokens, light/dark)
 * @see ADR-022 React 16/17-safe shared-lib code (navItemRepository/xrmContext this tab consumes)
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import { PinnedTab } from '../PinnedTab';

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
];

/** Rows "in the org" for a Monitored-group source entity (task 052) — the
 * fake server applies the `sprk_monitor eq true AND _ownerid_value eq {userId}`
 * filter itself so tests assert real filtering, not just mock plumbing. */
interface MonitoredFakeRecord {
  id: string;
  name: string;
  ownerId: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Fake Xrm
// ─────────────────────────────────────────────────────────────────────────────

interface FakeXrmOptions {
  pinRows?: typeof PIN_ROWS;
  noUtility?: boolean;
  /** Monitored-source rows per entity (task 052). Defaults to none. */
  monitoredRecordsByEntity?: Partial<Record<string, MonitoredFakeRecord[]>>;
}

function buildFakeXrm(options: FakeXrmOptions = {}) {
  const pinRows = options.pinRows ?? PIN_ROWS;
  const monitoredRecordsByEntity = options.monitoredRecordsByEntity ?? {};

  const MONITORED_NAME_FIELD: Record<string, string> = {
    sprk_matter: 'sprk_matternumber',
    sprk_project: 'sprk_projectnumber',
    sprk_document: 'sprk_documentname',
    sprk_todo: 'sprk_name',
    sprk_event: 'sprk_eventname',
    sprk_workassignment: 'sprk_workassignmentnumber',
    sprk_invoice: 'sprk_name',
  };

  const retrieveMultipleRecords = jest.fn(async (entity: string, query?: string) => {
    if (entity === 'sprk_navitem') {
      if (query?.includes(`sprk_type eq ${NavItemType.Pin}`)) {
        return { entities: pinRows };
      }
      return { entities: [] };
    }
    // Monitored-group source entities (task 052).
    if (query?.includes('sprk_monitor eq true') && query.includes('_ownerid_value eq')) {
      const rows = monitoredRecordsByEntity[entity] ?? [];
      const matched = rows.filter(r => r.ownerId === CURRENT_USER_ID);
      const idField = `${entity}id`;
      const nameField = MONITORED_NAME_FIELD[entity];
      return {
        entities: matched.map(r => ({ [idField]: r.id, [nameField]: r.name })),
      };
    }
    return { entities: [] };
  });

  const deleteRecord = jest.fn(async (_entity: string, _id: string) => ({}));
  const navigateTo = jest.fn(async () => undefined);

  const xrm: Record<string, unknown> = {
    WebApi: {
      retrieveMultipleRecords,
      retrieveRecord: jest.fn(),
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
      // Monitored group (task 052) — `monitoredService.ts` resolves the
      // display-name field via `getEntityMetadata`.
      getEntityMetadata: jest.fn(async (entity: string) => ({
        PrimaryNameAttribute: MONITORED_NAME_FIELD[entity] ?? 'name',
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

function renderPinnedTab(theme: 'light' | 'dark' = 'light') {
  return render(
    <FluentProvider theme={theme === 'light' ? webLightTheme : webDarkTheme}>
      <PinnedTab />
    </FluentProvider>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Suite
// ─────────────────────────────────────────────────────────────────────────────

describe('PinnedTab', () => {
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
  // Records group renders pins
  // ───────────────────────────────────────────────────────────────────────

  it('render_PinnedRows_ShowInRecordsGroupWithFilledStar', async () => {
    installMockXrm();
    renderPinnedTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('pinned-tab')).toBeInTheDocument();
    });

    expect(screen.getByTestId('pinned-tab-group-records')).toBeInTheDocument();
    expect(screen.getByTestId('pinned-tab-row-pin-matter')).toHaveTextContent('Acme v. Widget Co');
    expect(screen.getByTestId('pinned-tab-row-chip-pin-matter')).toHaveTextContent('Matter');
    expect(screen.getByTestId('pinned-tab-row-star-pin-matter')).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByTestId('pinned-tab-row-pin-document')).toBeInTheDocument();
  });

  it('render_DarkTheme_RendersAllPinnedRowsWithoutError', async () => {
    installMockXrm();
    expect(() => renderPinnedTab('dark')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('pinned-tab-row-pin-matter')).toBeInTheDocument();
    });
    expect(screen.getByTestId('pinned-tab-row-chip-pin-document')).toHaveTextContent('Document');
  });

  // ───────────────────────────────────────────────────────────────────────
  // Unstar removes the row (DELETE on sprk_navitem)
  // ───────────────────────────────────────────────────────────────────────

  it('click_UnpinStar_DeletesSprkNavitemAndRemovesRowFromRecords', async () => {
    const fakeXrm = installMockXrm();
    renderPinnedTab('light');
    const user = userEvent.setup();

    await waitFor(() => expect(screen.getByTestId('pinned-tab-row-star-pin-matter')).toBeInTheDocument());

    await user.click(screen.getByTestId('pinned-tab-row-star-pin-matter'));

    await waitFor(() => {
      expect(fakeXrm.WebApi.deleteRecord).toHaveBeenCalledWith('sprk_navitem', 'pin-matter');
    });

    await waitFor(() => {
      expect(screen.queryByTestId('pinned-tab-row-pin-matter')).not.toBeInTheDocument();
    });
    // The other row is untouched.
    expect(screen.getByTestId('pinned-tab-row-pin-document')).toBeInTheDocument();

    // Clicking the star must not also trigger row-click navigation.
    expect(fakeXrm.Navigation.navigateTo).not.toHaveBeenCalled();
  });

  // ───────────────────────────────────────────────────────────────────────
  // HARD MUST-NOT: sprk_monitor is never WRITTEN by the pin gesture
  // (task 052's Monitored group legitimately READS sprk_monitor via
  // retrieveMultipleRecords — this assertion is scoped to writes, and to the
  // Records/pin write path specifically, which only ever writes sprk_navitem)
  // ───────────────────────────────────────────────────────────────────────

  it('unpinGesture_NeverTouchesSprkMonitor', async () => {
    const fakeXrm = installMockXrm();
    renderPinnedTab('light');
    const user = userEvent.setup();

    await waitFor(() => expect(screen.getByTestId('pinned-tab-row-star-pin-matter')).toBeInTheDocument());
    await user.click(screen.getByTestId('pinned-tab-row-star-pin-matter'));

    await waitFor(() => expect(fakeXrm.WebApi.deleteRecord).toHaveBeenCalled());

    // No write call (create/update/delete) EVER targets sprk_monitor, or any
    // entity other than sprk_navitem — the pin gesture's only write surface.
    const writtenEntities = [
      ...fakeXrm.WebApi.createRecord.mock.calls,
      ...fakeXrm.WebApi.updateRecord.mock.calls,
      ...fakeXrm.WebApi.deleteRecord.mock.calls,
    ].map(call => call[0] as string);

    expect(writtenEntities).not.toContain('sprk_monitor');
    expect(writtenEntities.every(entity => entity === 'sprk_navitem')).toBe(true);

    // Reads are broader (Records' sprk_navitem query + the Monitored group's
    // own N per-entity sprk_monitor reads below), but never a raw "sprk_monitor"
    // pseudo-entity name and never a create/update/delete on any monitor-bearing entity.
    const readEntities = fakeXrm.WebApi.retrieveMultipleRecords.mock.calls.map(call => call[0] as string);
    expect(readEntities).not.toContain('sprk_monitor');
  });

  // ───────────────────────────────────────────────────────────────────────
  // Empty / no-Xrm / no-owner degradation
  // ───────────────────────────────────────────────────────────────────────

  it('render_NoPins_ShowsEmptyState', async () => {
    installMockXrm({ pinRows: [] });
    renderPinnedTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('pinned-tab-empty')).toHaveTextContent('Pinned records will appear here.');
    });
  });

  it('mount_NoUtilityOnXrm_DegradesToEmptyStateWithoutThrowing', async () => {
    installMockXrm({ noUtility: true });
    expect(() => renderPinnedTab('light')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('pinned-tab-empty')).toBeInTheDocument();
    });
  });

  it('mount_NoXrmAvailable_RendersEmptyStateWithoutThrowing', async () => {
    removeMockXrm();
    expect(() => renderPinnedTab('light')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('pinned-tab-empty')).toBeInTheDocument();
    });
  });

  // ───────────────────────────────────────────────────────────────────────
  // Monitored group (task 052, spec FR-09 / OQ-1b)
  // ───────────────────────────────────────────────────────────────────────

  describe('Monitored group (task 052)', () => {
    it('renders monitored records in a DISTINCT group, never merged with Records', async () => {
      installMockXrm({
        monitoredRecordsByEntity: {
          sprk_matter: [{ id: 'monitored-matter', name: 'MTR-777', ownerId: CURRENT_USER_ID }],
        },
      });
      renderPinnedTab('light');

      await waitFor(() => {
        expect(screen.getByTestId('pinned-tab-monitored')).toBeInTheDocument();
      });

      // Distinct group container, separate from Records.
      expect(screen.getByTestId('pinned-tab-group-records')).toBeInTheDocument();
      expect(screen.getByTestId('pinned-tab-group-monitored')).toBeInTheDocument();
      expect(screen.getByTestId('pinned-tab-monitored-row-sprk_matter-monitored-matter')).toHaveTextContent(
        'MTR-777'
      );

      // Records group is unaffected — still shows only the personal pins,
      // the monitored row does NOT appear inside the Records group.
      const recordsGroup = screen.getByTestId('pinned-tab-group-records');
      expect(recordsGroup).not.toHaveTextContent('MTR-777');
    });

    it('renders correctly in dark theme (ADR-021)', async () => {
      installMockXrm({
        monitoredRecordsByEntity: {
          sprk_document: [{ id: 'monitored-doc', name: 'Monitored.docx', ownerId: CURRENT_USER_ID }],
        },
      });
      expect(() => renderPinnedTab('dark')).not.toThrow();

      await waitFor(() => {
        expect(screen.getByTestId('pinned-tab-monitored-row-sprk_document-monitored-doc')).toBeInTheDocument();
      });
      expect(screen.getByTestId('pinned-tab-group-monitored')).toBeInTheDocument();
    });

    it('surfaces the shared-flag semantics copy (affects everyone, last-writer-wins)', async () => {
      installMockXrm();
      renderPinnedTab('light');

      await waitFor(() => {
        expect(screen.getByTestId('pinned-tab-monitored-semantics-note')).toBeInTheDocument();
      });
      const note = screen.getByTestId('pinned-tab-monitored-semantics-note');
      expect(note).toHaveTextContent(/affects everyone/i);
      expect(note).toHaveTextContent(/last change wins/i);
    });

    it('negative: a monitor=true record NOT owned by the current user is excluded', async () => {
      installMockXrm({
        monitoredRecordsByEntity: {
          sprk_matter: [
            { id: 'mine', name: 'MTR-MINE', ownerId: CURRENT_USER_ID },
            { id: 'not-mine', name: 'MTR-OTHER', ownerId: 'ffffffff-ffff-ffff-ffff-ffffffffffff' },
          ],
        },
      });
      renderPinnedTab('light');

      await waitFor(() => {
        expect(screen.getByTestId('pinned-tab-monitored-row-sprk_matter-mine')).toBeInTheDocument();
      });
      expect(screen.queryByTestId('pinned-tab-monitored-row-sprk_matter-not-mine')).not.toBeInTheDocument();
    });

    it('empty state when the user has no monitored records', async () => {
      installMockXrm();
      renderPinnedTab('light');

      await waitFor(() => {
        expect(screen.getByTestId('pinned-tab-monitored-empty')).toBeInTheDocument();
      });
    });

    it('the Monitored group has NO star/pin affordance (read-only lens)', async () => {
      installMockXrm({
        monitoredRecordsByEntity: {
          sprk_event: [{ id: 'monitored-event', name: 'Hearing', ownerId: CURRENT_USER_ID }],
        },
      });
      renderPinnedTab('light');

      await waitFor(() => {
        expect(screen.getByTestId('pinned-tab-monitored-row-sprk_event-monitored-event')).toBeInTheDocument();
      });
      // No unpin/pin star button anywhere inside the Monitored row.
      const row = screen.getByTestId('pinned-tab-monitored-row-sprk_event-monitored-event');
      expect(row.querySelector('button')).toBeNull();
    });

    it('clicking a monitored row navigates via Xrm.Navigation.navigateTo (entityrecord)', async () => {
      const fakeXrm = installMockXrm({
        monitoredRecordsByEntity: {
          sprk_invoice: [{ id: 'monitored-invoice', name: 'INV-100', ownerId: CURRENT_USER_ID }],
        },
      });
      renderPinnedTab('light');
      const user = userEvent.setup();

      await waitFor(() => {
        expect(screen.getByTestId('pinned-tab-monitored-row-sprk_invoice-monitored-invoice')).toBeInTheDocument();
      });
      await user.click(screen.getByTestId('pinned-tab-monitored-row-sprk_invoice-monitored-invoice'));

      await waitFor(() => {
        expect(fakeXrm.Navigation.navigateTo).toHaveBeenCalledWith({
          pageType: 'entityrecord',
          entityName: 'sprk_invoice',
          entityId: 'monitored-invoice',
        });
      });
    });

    it('no Xrm available: Monitored group degrades to empty state without throwing', async () => {
      removeMockXrm();
      expect(() => renderPinnedTab('light')).not.toThrow();

      await waitFor(() => {
        expect(screen.getByTestId('pinned-tab-monitored-empty')).toBeInTheDocument();
      });
    });
  });
});
