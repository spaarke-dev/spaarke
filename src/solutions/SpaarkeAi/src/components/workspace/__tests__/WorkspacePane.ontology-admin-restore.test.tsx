/**
 * WorkspacePane - restore wiring for the gated 'ontology-admin' tab (spaarke-ontology-platform-r1 task 104, D-22).
 *
 * The capability gate (`@spaarke/ai-widgets` ontologyAdminGate) registers the tab only after the BFF probe answers. WorkspacePane
 * must therefore (a) restore through `resolveRestorableWorkspaceWidget`, which skips a persisted admin tab the gate has not made
 * available, (b) wait for the gate before restoring a snapshot that holds an admin tab, and (c) NOT delay the tabs GET - or the
 * restore of a snapshot with no admin tab - on the gate.
 *
 * The gate itself (probe, registration, resolver logic) is covered in `ontologyAdminGate.test.tsx`; here it is a controllable fake
 * so the pane's wiring is what is under test.
 *
 * Assertions:
 *   (F3-1) gate unavailable: the admin tab is skipped, the other saved tabs restore.
 *   (F3-2) gate available: the admin tab is restored with the others.
 *   (F1-1) the tabs GET is issued while the gate is still pending; an admin-bearing snapshot restores only after the gate settles.
 *   (F1-2) a snapshot with no admin tab restores while the gate is still pending (nothing else waits on the probe).
 *   (F1-3) the local-snapshot fallback waits for the gate before restoring an admin tab, and skips it when the gate says no.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

// ---------------------------------------------------------------------------
// Controllable fakes: the tabs GET, and the ontology-admin gate.
// ---------------------------------------------------------------------------

interface SavedTab {
  id: string;
  widgetType: string;
  widgetData: unknown;
  displayName: string;
}
interface SavedSnapshot {
  tabs: SavedTab[];
  activeTabId: string | null;
}
let restoreSnapshot: SavedSnapshot = { tabs: [], activeTabId: null };
/** When true the tabs GET answers 404 (the benign "BFF has no tabs yet" case), driving the local-snapshot fallback. */
let tabsGetNotFound = false;

/** The fake gate: `gate.available` is what the real `isOntologyAdminTabAvailable()` would say once settled. */
const gate: { available: boolean; settle: () => void; promise: Promise<void> } = {
  available: false,
  settle: () => undefined,
  promise: Promise.resolve(),
};
function armGate(available: boolean): void {
  gate.available = available;
  gate.promise = new Promise<void>(resolve => {
    gate.settle = resolve;
  });
}

const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
  const method = init?.method ?? 'GET';
  if (method === 'GET' && url.includes('/tabs')) {
    if (tabsGetNotFound) throw httpError(404);
    return { ok: true, status: 200, json: async () => restoreSnapshot } as Partial<Response> as Response;
  }
  if (method === 'PATCH' && url.includes('/tabs')) {
    return { ok: true, status: 204, json: async () => ({}) } as Partial<Response> as Response;
  }
  throw httpError(404);
});

let mockEntityContext: { entityType: string; entityId: string } | null = null;

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;

  const makeStub = (widgetType: string): React.FC<{ data?: unknown }> => {
    return function StubWidget(): React.JSX.Element {
      return <div data-testid={`widget-stub-${widgetType}`}>{widgetType}</div>;
    };
  };

  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn().mockResolvedValue('test-token'),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: 'session-ontology-restore',
      setChatSessionId: jest.fn(),
      playbookId: undefined,
      setPlaybookId: jest.fn(),
      entityContext: mockEntityContext,
      contextMapping: null,
      isLoadingContextMapping: false,
      streaming: { onPaneEvent: null },
      streamingState: { isStreaming: false, tokenCount: 0 },
      turnCount: 0,
      isLoading: false,
    }),
    // The ungated resolver: resolves EVERY type, the admin tab included. A pane that restored through this would restore
    // the admin tab whatever the gate says - which is exactly what F3-1 / F1-3 must catch.
    resolveWorkspaceWidget: jest.fn(async (widgetType: string) => makeStub(widgetType)),
    // The gated resolver, as the real one behaves: an admin tab resolves only when the gate says it is available.
    resolveRestorableWorkspaceWidget: jest.fn(async (widgetType: string) =>
      widgetType === 'ontology-admin' && !gate.available ? null : makeStub(widgetType),
    ),
    whenOntologyAdminGateSettled: jest.fn(() => gate.promise),
    getWorkspaceWidgetMetadata: jest.fn(() => ({
      displayName: 'Workspace',
      category: 'workspace',
      defaultOrder: 100,
      allowMultiple: true,
    })),
  };
});

jest.mock('../../../hooks/useWorkspaceLayouts', () => ({
  useWorkspaceLayouts: () => ({
    layouts: [
      { id: 'layout-default', name: 'Daily Briefing', isSystem: true },
      { id: 'layout-compose', name: 'Compose', isSystem: true },
    ],
    activeLayout: { id: 'layout-default', name: 'Daily Briefing', isSystem: true },
    isLoading: false,
    refetch: jest.fn(),
    setActiveLayoutById: jest.fn(),
  }),
}));

jest.mock('../../../services/pinnedWorkspaces', () => ({
  getPinnedWorkspaces: jest.fn(() => []),
  prunePinnedToKnown: jest.fn(() => []),
  isPinned: jest.fn(() => false),
  pinWorkspace: jest.fn(),
  unpinWorkspace: jest.fn(),
  setPinnedWorkspacesOrder: jest.fn(),
  moveWorkspaceToTop: jest.fn(),
}));

jest.mock('@spaarke/ui-components', () => {
  // Spread the real module - @spaarke/ai-widgets' widget registry imports safeRegister from here at module-init time.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ui-components') as any;
  return {
    ...actual,
    PaneHeader: ({ title, rightSlot }: { title: string; rightSlot?: React.ReactNode }) => (
      <div data-testid="pane-header">
        <span>{title}</span>
        {rightSlot}
      </div>
    ),
  };
});

// Import AFTER mocks so module resolution picks them up.
import { WorkspacePane, tabAnchorKeyForContext } from '../WorkspacePane';
import { httpError } from '../../../__tests__/helpers/httpError';

const COMPOSE_TAB: SavedTab = {
  id: 'wstab-2-workspace',
  widgetType: 'workspace',
  widgetData: { layoutId: 'layout-compose', layoutName: 'Compose' },
  displayName: 'Compose',
};
const ADMIN_TAB: SavedTab = {
  id: 'wstab-3-ontology-admin',
  widgetType: 'ontology-admin',
  widgetData: {},
  displayName: 'Ontology admin',
};
const ANCHOR = { entityType: 'sprk_analysis', entityId: 'ANALYSIS-1' };

function renderPane(): void {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <WorkspacePane />
      </PaneEventBusProvider>
    </FluentProvider>,
  );
}

function tabsGetCalls(): number {
  return authenticatedFetchMock.mock.calls.filter(
    ([url, init]) => (init?.method ?? 'GET') === 'GET' && String(url).includes('/tabs'),
  ).length;
}

/** Seeds the local anchor-keyed snapshot the 404 fallback reads. */
function seedLocalSnapshot(snapshot: SavedSnapshot): void {
  const key = tabAnchorKeyForContext(ANCHOR);
  if (!key) throw new Error('test setup: no anchor key');
  localStorage.setItem(key, JSON.stringify(snapshot));
}

async function pause(ms: number): Promise<void> {
  await act(async () => {
    await new Promise(resolve => setTimeout(resolve, ms));
  });
}

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  restoreSnapshot = { tabs: [], activeTabId: null };
  tabsGetNotFound = false;
  mockEntityContext = null;
  try {
    localStorage.clear();
  } catch {
    /* storage unavailable */
  }
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

describe("WorkspacePane - restore of the gated 'ontology-admin' tab", () => {
  it('F3-1: gate unavailable - the saved admin tab is skipped and the other tabs restore', async () => {
    armGate(false);
    restoreSnapshot = { tabs: [COMPOSE_TAB, ADMIN_TAB], activeTabId: COMPOSE_TAB.id };
    gate.settle();

    renderPane();

    expect(await screen.findByRole('tab', { name: /compose/i }, { timeout: 3000 })).toBeInTheDocument();
    // the default-layout install (a later step) has run, so restore is over: the admin tab was skipped, not merely late
    expect(await screen.findByRole('tab', { name: /daily briefing/i }, { timeout: 3000 })).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /ontology admin/i })).not.toBeInTheDocument();
  });

  it('F3-2: gate available - the saved admin tab is restored with the others', async () => {
    armGate(true);
    restoreSnapshot = { tabs: [COMPOSE_TAB, ADMIN_TAB], activeTabId: COMPOSE_TAB.id };
    gate.settle();

    renderPane();

    expect(await screen.findByRole('tab', { name: /compose/i }, { timeout: 3000 })).toBeInTheDocument();
    expect(await screen.findByRole('tab', { name: /ontology admin/i }, { timeout: 3000 })).toBeInTheDocument();
  });

  it('F1-1: the tabs GET is issued while the gate is pending; an admin-bearing snapshot restores only after the gate settles', async () => {
    armGate(true);
    restoreSnapshot = { tabs: [COMPOSE_TAB, ADMIN_TAB], activeTabId: COMPOSE_TAB.id };

    renderPane();

    // the GET went out although the gate has not answered (it is not awaited ahead of the GET)
    await waitFor(() => expect(tabsGetCalls()).toBe(1));
    // ...but the admin-bearing restore is held for the gate: nothing restored yet
    await pause(50);
    expect(screen.queryByRole('tab', { name: /compose/i })).not.toBeInTheDocument();

    await act(async () => {
      gate.settle();
    });
    expect(await screen.findByRole('tab', { name: /ontology admin/i }, { timeout: 3000 })).toBeInTheDocument();
    expect(await screen.findByRole('tab', { name: /compose/i }, { timeout: 3000 })).toBeInTheDocument();
  });

  it('F1-2: a snapshot with no admin tab restores while the gate is still pending', async () => {
    armGate(false); // never settled in this test
    restoreSnapshot = { tabs: [COMPOSE_TAB], activeTabId: COMPOSE_TAB.id };

    renderPane();

    expect(await screen.findByRole('tab', { name: /compose/i }, { timeout: 3000 })).toBeInTheDocument();
    expect(await screen.findByRole('tab', { name: /daily briefing/i }, { timeout: 3000 })).toBeInTheDocument();
  });

  describe('local-snapshot fallback (the tabs GET answers 404)', () => {
    it('F1-3: waits for the gate before restoring an admin tab, then restores it when the gate says yes', async () => {
      armGate(true);
      tabsGetNotFound = true;
      mockEntityContext = ANCHOR;
      seedLocalSnapshot({ tabs: [COMPOSE_TAB, ADMIN_TAB], activeTabId: COMPOSE_TAB.id });

      renderPane();

      await waitFor(() => expect(tabsGetCalls()).toBe(1));
      await pause(50);
      expect(screen.queryByRole('tab', { name: /compose/i })).not.toBeInTheDocument();

      await act(async () => {
        gate.settle();
      });
      expect(await screen.findByRole('tab', { name: /ontology admin/i }, { timeout: 3000 })).toBeInTheDocument();
      expect(await screen.findByRole('tab', { name: /compose/i }, { timeout: 3000 })).toBeInTheDocument();
    });

    it('F1-3: skips the admin tab when the gate says no, and restores the others', async () => {
      armGate(false);
      tabsGetNotFound = true;
      mockEntityContext = ANCHOR;
      seedLocalSnapshot({ tabs: [COMPOSE_TAB, ADMIN_TAB], activeTabId: COMPOSE_TAB.id });
      gate.settle();

      renderPane();

      expect(await screen.findByRole('tab', { name: /compose/i }, { timeout: 3000 })).toBeInTheDocument();
      expect(await screen.findByRole('tab', { name: /daily briefing/i }, { timeout: 3000 })).toBeInTheDocument();
      expect(screen.queryByRole('tab', { name: /ontology admin/i })).not.toBeInTheDocument();
    });
  });
});
