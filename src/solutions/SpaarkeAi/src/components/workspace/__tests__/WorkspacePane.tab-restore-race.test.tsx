/**
 * WorkspacePane — NFR-09 tab-restore vs auto-install-default sequencing tests
 *
 * G-P3 UAT round-3 R3-3 (spaarke-ai-architecture-redesign-r1, 2026-07-07).
 *
 * Defect: chat-opened workspace tabs (the `workspace_open_tab` SSE bridge) and
 * menu-opened tabs were BOTH persisted correctly by the debounced PATCH
 * write-through — but on page refresh every persisted tab was lost. App
 * Insights pinned the sequence: SaveTabs tabCount=2 (16:48:10Z, tab opened via
 * chat) → refresh GET /tabs (16:49:05Z) → SaveTabs tabCount=1 with a
 * freshly-numbered `wstab-1-workspace` id (16:49:07Z). The auto-install-default
 * effect's `addTab` raced the async restore effect: it landed first, which
 * (a) made `restoreFromPersistence`'s hasNonHomeTab guard silently no-op —
 * dropping the restored tabs — and (b) fired the write-through, OVERWRITING
 * the server store with only the fresh default tab.
 *
 * Fix under test: WorkspacePane's `tabRestoreSettled` gate — the
 * auto-install-default and pin auto-open effects wait until the restore
 * effect settles (success, 404, or error) before dispatching.
 *
 * Assertions:
 *   (R3-3-1) A persisted workspace tab is restored on mount even when the
 *            restore GET resolves SLOWER than the layouts fetch (the
 *            production race), and the default layout tab is added after it.
 *   (R3-3-2) No PATCH write-through ever omits the restored tab — the
 *            pre-fix failure was a PATCH carrying only the default tab.
 *   (R3-3-3) When the persisted snapshot already contains the default
 *            layout, auto-install skips it (no duplicate tab).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

// ---------------------------------------------------------------------------
// Controllable fetch mock — GET /tabs resolves on a REAL macrotask delay so
// it loses the race against the (synchronously mocked) layouts fetch, exactly
// like production. PATCH bodies are recorded for the write-through assertions.
// ---------------------------------------------------------------------------

interface RecordedPatch {
  tabs: Array<{ id: string; widgetType: string }>;
  activeTabId: string | null;
}

const recordedPatches: RecordedPatch[] = [];

let restoreSnapshot: {
  tabs: Array<{
    id: string;
    widgetType: string;
    widgetData: unknown;
    displayName: string;
  }>;
  activeTabId: string | null;
} = { tabs: [], activeTabId: null };

const authenticatedFetchMock = jest.fn(
  async (url: string, init?: RequestInit): Promise<Response> => {
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.includes('/tabs')) {
      // Delay the restore GET past several macrotask turns so the
      // auto-install effect's setTimeout(0) dispatch would win the race
      // WITHOUT the tabRestoreSettled gate (the pre-fix production timing).
      await new Promise(resolve => setTimeout(resolve, 25));
      return {
        ok: true,
        status: 200,
        json: async () => restoreSnapshot,
      } as Partial<Response> as Response;
    }
    if (method === 'PATCH' && url.includes('/tabs')) {
      recordedPatches.push(JSON.parse(String(init?.body)) as RecordedPatch);
      return {
        ok: true,
        status: 204,
        json: async () => ({}),
      } as Partial<Response> as Response;
    }
    return {
      ok: false,
      status: 404,
      json: async () => ({}),
    } as Partial<Response> as Response;
  },
);

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
      chatSessionId: 'session-restore-race',
      setChatSessionId: jest.fn(),
      playbookId: undefined,
      setPlaybookId: jest.fn(),
      entityContext: null,
      contextMapping: null,
      isLoadingContextMapping: false,
      streaming: { onPaneEvent: null },
      streamingState: { isStreaming: false, tokenCount: 0 },
      turnCount: 0,
      isLoading: false,
    }),
    resolveWorkspaceWidget: jest.fn(async (widgetType: string) => makeStub(widgetType)),
    getWorkspaceWidgetMetadata: jest.fn(() => ({
      displayName: 'Workspace',
      category: 'workspace',
      defaultOrder: 100,
      allowMultiple: true,
    })),
  };
});

// Default layout available (the production default is "Daily Briefing" in dev)
// — this is the tab whose auto-install raced (and pre-fix, clobbered) restore.
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
  // Spread the real module — @spaarke/ai-widgets' widget registry imports
  // safeRegister from here at module-init time (a bare-object mock breaks
  // the requireActual('@spaarke/ai-widgets') chain above).
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
import { WorkspacePane } from '../WorkspacePane';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderPane(): { bus: PaneEventBus } {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <WorkspacePane />
      </PaneEventBusProvider>
    </FluentProvider>,
  );
  return { bus };
}

beforeEach(() => {
  recordedPatches.length = 0;
  authenticatedFetchMock.mockClear();
  restoreSnapshot = { tabs: [], activeTabId: null };
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

// ---------------------------------------------------------------------------
// (R3-3-1 / R3-3-2) Restored tab survives the auto-install race + is never
// dropped from a write-through PATCH
// ---------------------------------------------------------------------------

describe('WorkspacePane — tab restore vs auto-install sequencing (R3-3)', () => {
  it('restores the persisted workspace tab BEFORE auto-installing the default, and never PATCHes it away', async () => {
    restoreSnapshot = {
      tabs: [
        {
          id: 'wstab-2-workspace',
          widgetType: 'workspace',
          widgetData: { layoutId: 'layout-compose', layoutName: 'Compose' },
          displayName: 'Compose',
        },
      ],
      activeTabId: 'wstab-2-workspace',
    };

    renderPane();

    // The restored Compose tab appears (restore completed)…
    const composeTab = await screen.findByRole(
      'tab',
      { name: /compose/i },
      { timeout: 3000 },
    );
    expect(composeTab).toBeInTheDocument();

    // …AND the default layout tab is auto-installed AFTER it (it was not
    // already open, so the gated effect still adds it).
    const defaultTab = await screen.findByRole(
      'tab',
      { name: /daily briefing/i },
      { timeout: 3000 },
    );
    expect(defaultTab).toBeInTheDocument();

    // Write-through integrity: wait for the auto-install's debounced PATCH,
    // then require EVERY recorded PATCH to still carry the restored tab —
    // the pre-fix failure mode was a PATCH containing ONLY the fresh default
    // tab (tabCount 2 → 1 in the round-3 App Insights evidence).
    await waitFor(
      () => {
        expect(recordedPatches.length).toBeGreaterThan(0);
      },
      { timeout: 3000 },
    );
    for (const patch of recordedPatches) {
      const ids = patch.tabs.map(t => t.id);
      expect(ids).toContain('wstab-2-workspace');
      // R3-3 follow-on: the auto-installed default must NOT reuse a restored
      // tab's id (restoreFromPersistence advances _nextSeq past restored seqs).
      expect(new Set(ids).size).toBe(ids.length);
    }
  });

  // -------------------------------------------------------------------------
  // (R3-3-3) Default already restored ⇒ auto-install skips (no duplicate)
  // -------------------------------------------------------------------------

  it('skips the default auto-install when the restored snapshot already contains that layout', async () => {
    restoreSnapshot = {
      tabs: [
        {
          id: 'wstab-1-workspace',
          widgetType: 'workspace',
          widgetData: { layoutId: 'layout-default', layoutName: 'Daily Briefing' },
          displayName: 'Daily Briefing',
        },
      ],
      activeTabId: 'wstab-1-workspace',
    };

    renderPane();

    const defaultTab = await screen.findByRole(
      'tab',
      { name: /daily briefing/i },
      { timeout: 3000 },
    );
    expect(defaultTab).toBeInTheDocument();

    // Give the (gated) auto-install effect time to run, then assert exactly
    // ONE Daily Briefing tab exists — the alreadyOpen check now sees the
    // restored tab because the gate sequenced it after restore.
    await new Promise(resolve => setTimeout(resolve, 100));
    const briefingTabs = screen.getAllByRole('tab', { name: /daily briefing/i });
    expect(briefingTabs).toHaveLength(1);
  });
});
