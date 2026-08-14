/**
 * MonitoredTab Component Tests
 * (spaarke-side-pane-navigation-history-r1 task 052, spec FR-09 / OQ-1b;
 * EXTRACTED from `PinnedTab.test.tsx`'s "Monitored group" describe block by
 * the UAT-driven redesign, which promoted Monitored to its own top-level tab.)
 *
 * Verifies the closed acceptance-criteria set:
 *   - Lists the user's OWNED, `sprk_monitor=true` records.
 *   - A `sprk_monitor=true` record NOT owned by the user is excluded.
 *   - The tab surfaces shared-flag semantics copy (affects everyone,
 *     last-writer-wins).
 *   - Renders correctly in light AND dark themes (ADR-021).
 *   - No star/pin affordance anywhere (read-only lens).
 *   - Clicking a row navigates via `Xrm.Navigation.navigateTo` (entityrecord).
 *   - Each row shows a far-left record-type icon (replaces the removed chip).
 *   - No-Xrm degrades to a safe empty state, never throws.
 *
 * `window.Xrm` is installed directly (mirrors `RecentTab.test.tsx`) so
 * `getXrm()` resolves it via the normal frame-walk — no module mock of
 * `monitoredService`; the fake `Xrm.WebApi` drives the real
 * `listMonitoredByMe` code path.
 *
 * @see ../MonitoredTab.tsx
 * @see ../../services/monitoredService.ts
 * @see ADR-021 Fluent UI v9 design system (tokens, light/dark)
 * @see ADR-022 React 16/17-safe shared-lib code (xrmContext this tab consumes)
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import { MonitoredTab } from '../MonitoredTab';

// ─────────────────────────────────────────────────────────────────────────────
// Fixtures
// ─────────────────────────────────────────────────────────────────────────────

const CURRENT_USER_ID = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

/** Rows "in the org" for a Monitored-source entity — the fake server applies
 * the `sprk_monitor eq true AND _ownerid_value eq {userId}` filter itself so
 * tests assert real filtering, not just mock plumbing. */
interface MonitoredFakeRecord {
  id: string;
  name: string;
  ownerId: string;
}

const MONITORED_NAME_FIELD: Record<string, string> = {
  sprk_matter: 'sprk_matternumber',
  sprk_project: 'sprk_projectnumber',
  sprk_document: 'sprk_documentname',
  sprk_todo: 'sprk_name',
  sprk_event: 'sprk_eventname',
  sprk_workassignment: 'sprk_workassignmentnumber',
  sprk_invoice: 'sprk_name',
};

// ─────────────────────────────────────────────────────────────────────────────
// Fake Xrm
// ─────────────────────────────────────────────────────────────────────────────

interface FakeXrmOptions {
  monitoredRecordsByEntity?: Partial<Record<string, MonitoredFakeRecord[]>>;
  noUtility?: boolean;
}

function buildFakeXrm(options: FakeXrmOptions = {}) {
  const monitoredRecordsByEntity = options.monitoredRecordsByEntity ?? {};

  const retrieveMultipleRecords = jest.fn(async (entity: string, query?: string) => {
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

  const navigateTo = jest.fn(async () => undefined);

  const xrm: Record<string, unknown> = {
    WebApi: {
      retrieveMultipleRecords,
      retrieveRecord: jest.fn(),
      createRecord: jest.fn(),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
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

function renderMonitoredTab(theme: 'light' | 'dark' = 'light') {
  return render(
    <FluentProvider theme={theme === 'light' ? webLightTheme : webDarkTheme}>
      <MonitoredTab />
    </FluentProvider>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Suite
// ─────────────────────────────────────────────────────────────────────────────

describe('MonitoredTab', () => {
  const originalXrm = (window as unknown as { Xrm?: unknown }).Xrm;

  afterEach(() => {
    if (originalXrm) {
      (window as unknown as { Xrm: unknown }).Xrm = originalXrm;
    } else {
      removeMockXrm();
    }
    jest.clearAllMocks();
  });

  it('renders the user\'s monitored records with a far-left icon (replaces the removed chip)', async () => {
    installMockXrm({
      monitoredRecordsByEntity: {
        sprk_matter: [{ id: 'monitored-matter', name: 'MTR-777', ownerId: CURRENT_USER_ID }],
      },
    });
    renderMonitoredTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('monitored-tab')).toBeInTheDocument();
    });

    const rowId = 'sprk_matter-monitored-matter';
    expect(screen.getByTestId(`monitored-tab-row-${rowId}`)).toHaveTextContent('MTR-777');
    expect(screen.getByTestId(`monitored-tab-row-icon-${rowId}`)).toBeInTheDocument();
    expect(screen.getByTestId(`monitored-tab-row-icon-${rowId}`).querySelector('svg')).not.toBeNull();

    // No Badge chip anywhere anymore.
    expect(screen.queryByTestId(`pinned-tab-monitored-row-chip-${rowId}`)).not.toBeInTheDocument();
  });

  it('renders correctly in dark theme (ADR-021)', async () => {
    installMockXrm({
      monitoredRecordsByEntity: {
        sprk_document: [{ id: 'monitored-doc', name: 'Monitored.docx', ownerId: CURRENT_USER_ID }],
      },
    });
    expect(() => renderMonitoredTab('dark')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('monitored-tab-row-sprk_document-monitored-doc')).toBeInTheDocument();
    });
  });

  it('surfaces the shared-flag semantics copy (affects everyone, last-writer-wins)', async () => {
    installMockXrm();
    renderMonitoredTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('monitored-tab-semantics-note')).toBeInTheDocument();
    });
    const note = screen.getByTestId('monitored-tab-semantics-note');
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
    renderMonitoredTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('monitored-tab-row-sprk_matter-mine')).toBeInTheDocument();
    });
    expect(screen.queryByTestId('monitored-tab-row-sprk_matter-not-mine')).not.toBeInTheDocument();
  });

  it('empty state when the user has no monitored records', async () => {
    installMockXrm();
    renderMonitoredTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('monitored-tab-empty')).toBeInTheDocument();
    });
  });

  it('has NO star/pin affordance anywhere (read-only lens)', async () => {
    installMockXrm({
      monitoredRecordsByEntity: {
        sprk_event: [{ id: 'monitored-event', name: 'Hearing', ownerId: CURRENT_USER_ID }],
      },
    });
    renderMonitoredTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('monitored-tab-row-sprk_event-monitored-event')).toBeInTheDocument();
    });
    const row = screen.getByTestId('monitored-tab-row-sprk_event-monitored-event');
    expect(row.querySelector('button')).toBeNull();
  });

  it('clicking a monitored row navigates via Xrm.Navigation.navigateTo (entityrecord)', async () => {
    const fakeXrm = installMockXrm({
      monitoredRecordsByEntity: {
        sprk_invoice: [{ id: 'monitored-invoice', name: 'INV-100', ownerId: CURRENT_USER_ID }],
      },
    });
    renderMonitoredTab('light');
    const user = userEvent.setup();

    await waitFor(() => {
      expect(screen.getByTestId('monitored-tab-row-sprk_invoice-monitored-invoice')).toBeInTheDocument();
    });
    await user.click(screen.getByTestId('monitored-tab-row-sprk_invoice-monitored-invoice'));

    await waitFor(() => {
      expect(fakeXrm.Navigation.navigateTo).toHaveBeenCalledWith({
        pageType: 'entityrecord',
        entityName: 'sprk_invoice',
        entityId: 'monitored-invoice',
      });
    });
  });

  it('keydown Enter on a row also navigates', async () => {
    const fakeXrm = installMockXrm({
      monitoredRecordsByEntity: {
        sprk_todo: [{ id: 'monitored-todo', name: 'Follow up', ownerId: CURRENT_USER_ID }],
      },
    });
    renderMonitoredTab('light');
    const user = userEvent.setup();

    await waitFor(() => {
      expect(screen.getByTestId('monitored-tab-row-sprk_todo-monitored-todo')).toBeInTheDocument();
    });
    screen.getByTestId('monitored-tab-row-sprk_todo-monitored-todo').focus();
    await user.keyboard('{Enter}');

    expect(fakeXrm.Navigation.navigateTo).toHaveBeenCalledWith({
      pageType: 'entityrecord',
      entityName: 'sprk_todo',
      entityId: 'monitored-todo',
    });
  });

  it('no Xrm available: degrades to empty state without throwing', async () => {
    removeMockXrm();
    expect(() => renderMonitoredTab('light')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('monitored-tab-empty')).toBeInTheDocument();
    });
  });

  it('no Utility on Xrm: degrades to empty state without throwing', async () => {
    installMockXrm({ noUtility: true });
    expect(() => renderMonitoredTab('light')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('monitored-tab-empty')).toBeInTheDocument();
    });
  });
});
