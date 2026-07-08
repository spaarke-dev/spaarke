/**
 * WorkspacePane — compose-launch relaunch activation tests (issue #572 Defect 1d).
 *
 * Defect: when SpaarkeAi is launched with `composeMode=editor` (ribbon
 * Open-in-Compose modal) and the NFR-09 tab restore brings back a session
 * where the Compose layout tab is ALREADY OPEN but NOT ACTIVE, the
 * auto-install-default effect saw `alreadyOpen === true` and returned WITHOUT
 * activating it — violating its own "we always want the Compose layout on
 * top" rule. Because the tab strip is hidden in compose-launch mode, the user
 * was stranded on whatever tab the persisted `activeTabId` pointed at (the
 * normal three-pane workspace) with no way to reach the editor.
 *
 * Fix under test: the auto-install effect now calls
 * `manager.setActiveTab(existingTab.id)` (+ state sync + tab_change dispatch)
 * for the already-open Compose tab in compose-launch mode instead of
 * returning early.
 *
 * Harness copied from WorkspacePane.tab-restore-race.test.tsx (same restore
 * GET delay to reproduce the production effect ordering).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

// ---------------------------------------------------------------------------
// Controllable fetch mock — GET /tabs resolves on a real macrotask delay
// (mirrors the tab-restore-race harness). PATCH bodies are recorded so we can
// assert the activation write-through carries the Compose tab as active.
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

  // Stub renders the layoutName from widgetData so the test can tell WHICH
  // workspace tab's widget is currently active (only the active tab renders).
  const makeStub = (widgetType: string): React.FC<{ data?: unknown }> => {
    return function StubWidget({ data }: { data?: unknown }): React.JSX.Element {
      const layoutName = (data as { layoutName?: string } | null)?.layoutName ?? widgetType;
      return <div data-testid="active-widget-stub">{layoutName}</div>;
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
      chatSessionId: 'session-compose-relaunch',
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

// Compose-launch mode: `useComposeLaunch` reports the editor launch, and the
// pane-collapse context is absent (modal host).
jest.mock('../../shell/ThreePaneShell', () => ({
  usePaneCollapseContext: () => null,
  useComposeLaunch: () => ({
    composeMode: 'editor',
    document: null,
    driveId: undefined,
  }),
}));

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
// Issue #572 Defect 1d — compose relaunch activates the restored Compose tab
// ---------------------------------------------------------------------------

describe('WorkspacePane — compose-launch relaunch activation (#572 Defect 1d)', () => {
  it('activates the ALREADY-OPEN Compose tab when relaunched in composeMode=editor (does not strand the user on the restored active tab)', async () => {
    // Persisted session: Compose tab open but Daily Briefing is the
    // persisted active tab — the NFR-09 restore honors that activeTabId.
    restoreSnapshot = {
      tabs: [
        {
          id: 'wstab-1-workspace',
          widgetType: 'workspace',
          widgetData: { layoutId: 'layout-default', layoutName: 'Daily Briefing' },
          displayName: 'Daily Briefing',
        },
        {
          id: 'wstab-2-workspace',
          widgetType: 'workspace',
          widgetData: { layoutId: 'layout-compose', layoutName: 'Compose' },
          displayName: 'Compose',
        },
      ],
      activeTabId: 'wstab-1-workspace',
    };

    renderPane();

    // The active widget must end up being the COMPOSE layout — pre-fix the
    // auto-install effect early-returned on alreadyOpen and the user was left
    // on Daily Briefing with the tab strip hidden (no way to switch).
    await waitFor(
      () => {
        expect(screen.getByTestId('active-widget-stub')).toHaveTextContent('Compose');
      },
      { timeout: 3000 },
    );

    // Compose-launch mode hides the tab strip — no tabs are rendered, which
    // is exactly why the activation (not just install-skip) matters.
    expect(screen.queryAllByRole('tab')).toHaveLength(0);

    // The activation is persisted: the last write-through PATCH carries the
    // Compose tab as the active tab.
    await waitFor(
      () => {
        expect(recordedPatches.length).toBeGreaterThan(0);
        expect(recordedPatches[recordedPatches.length - 1]?.activeTabId).toBe(
          'wstab-2-workspace',
        );
      },
      { timeout: 3000 },
    );
  });

  it('does not duplicate the Compose tab on relaunch (activation replaces the install)', async () => {
    restoreSnapshot = {
      tabs: [
        {
          id: 'wstab-1-workspace',
          widgetType: 'workspace',
          widgetData: { layoutId: 'layout-default', layoutName: 'Daily Briefing' },
          displayName: 'Daily Briefing',
        },
        {
          id: 'wstab-2-workspace',
          widgetType: 'workspace',
          widgetData: { layoutId: 'layout-compose', layoutName: 'Compose' },
          displayName: 'Compose',
        },
      ],
      activeTabId: 'wstab-1-workspace',
    };

    renderPane();

    await waitFor(
      () => {
        expect(screen.getByTestId('active-widget-stub')).toHaveTextContent('Compose');
      },
      { timeout: 3000 },
    );

    // Every write-through PATCH keeps exactly ONE Compose tab (no re-install
    // stacked a duplicate).
    await waitFor(
      () => {
        expect(recordedPatches.length).toBeGreaterThan(0);
      },
      { timeout: 3000 },
    );
    for (const patch of recordedPatches) {
      const composeTabs = patch.tabs.filter(t => t.id === 'wstab-2-workspace');
      expect(composeTabs).toHaveLength(1);
      expect(patch.tabs).toHaveLength(2);
    }
  });
});
