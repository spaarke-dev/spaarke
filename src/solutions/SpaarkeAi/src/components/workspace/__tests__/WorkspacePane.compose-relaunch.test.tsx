/**
 * WorkspacePane — compose-launch unification tests (spaarkeai-compose-r2).
 *
 * UNIFY (completes the R1 flip): the ribbon `composeMode=editor` launch
 * (Open-in-Compose modal) now opens a first-class DIRECT `'compose'` widget tab
 * (widgetType 'compose'), NOT the "Compose" workspace LAYOUT tab
 * (widgetType 'workspace' + layoutName 'Compose'). Every Compose mount is
 * therefore protected by the keep-mounted-hidden keep-alive
 * (WorkspaceTabManagerComponent) — opening another tab (e.g. Email) no longer
 * unmounts the loaded document.
 *
 * Contracts asserted:
 *   1. A FRESH compose-launch (no restore) opens a widgetType 'compose' tab,
 *      makes it active, and maps the ribbon stored-document launch context
 *      (composeLaunch.document + .driveId) onto the compose SEED
 *      (widgetData.compose.{speDriveItemId,sprkDocumentId,speDriveId,fileName})
 *      with the filename hoisted to the top-level server-readable `filename`.
 *      The BFF default layout (Daily Briefing) is NOT installed behind it.
 *   2. RELAUNCH where a compose tab was restored (issue #572 Defect 1d): the
 *      restored 'compose' tab is REUSED + ACTIVATED (not stranded on the
 *      persisted active tab), with NO duplicate compose tab.
 *
 * A regression that mounts Compose as a 'workspace' layout tab fails contract
 * (1)'s widgetType assertion.
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
// (mirrors the tab-restore-race harness). PATCH bodies are recorded (incl.
// per-tab widgetData) so we can assert the compose tab is created/activated
// with the correct seed.
// ---------------------------------------------------------------------------

interface RecordedPatch {
  tabs: Array<{ id: string; widgetType: string; widgetData: unknown }>;
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

  // Stub renders the widgetType so the test can tell WHICH tab's widget is
  // currently rendered (the compose keep-alive keeps the compose stub mounted).
  const makeStub = (widgetType: string): React.FC<{ data?: unknown }> => {
    return function StubWidget(): React.JSX.Element {
      return <div data-testid={`active-widget-${widgetType}`}>{widgetType}</div>;
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
      chatSessionId: 'session-compose-unify',
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

// Compose-launch mode: `useComposeLaunch` reports the editor launch carrying a
// stored-document ref (the ribbon Open-in-Compose contract). Pane-collapse
// context is absent (modal host).
jest.mock('../../shell/ThreePaneShell', () => ({
  usePaneCollapseContext: () => null,
  useComposeLaunch: () => ({
    composeMode: 'editor',
    document: {
      speDriveItemId: 'drive-item-1',
      sprkDocumentId: 'doc-1',
      fileName: 'Brief.docx',
    },
    driveId: 'drive-1',
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

const composeTabsIn = (patch: RecordedPatch) =>
  patch.tabs.filter(t => t.widgetType === 'compose');

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
// Contract 1 — fresh compose-launch opens a DIRECT 'compose' tab with the seed
// ---------------------------------------------------------------------------

describe('WorkspacePane — compose-launch UNIFY (widgetType compose)', () => {
  it('a fresh composeMode=editor launch opens a widgetType:compose tab carrying the ribbon stored-doc seed (not a workspace layout tab)', async () => {
    renderPane();

    // The Compose DIRECT widget mounts (kept-alive host renders it).
    await waitFor(
      () => {
        expect(screen.getByTestId('active-widget-compose')).toBeInTheDocument();
      },
      { timeout: 3000 },
    );

    // The BFF default (Daily Briefing) must NOT be installed behind Compose.
    expect(screen.queryByTestId('active-widget-workspace')).not.toBeInTheDocument();
    // Compose-launch mode hides the tab strip.
    expect(screen.queryAllByRole('tab')).toHaveLength(0);

    // The persisted state has exactly ONE compose tab, active, with the seed
    // mapped from the launch context (+ filename hoisted to the top level).
    await waitFor(
      () => {
        const last = recordedPatches[recordedPatches.length - 1];
        expect(last).toBeDefined();
        expect(composeTabsIn(last)).toHaveLength(1);
      },
      { timeout: 3000 },
    );
    const last = recordedPatches[recordedPatches.length - 1];
    const composeTab = composeTabsIn(last)[0];
    expect(last.activeTabId).toBe(composeTab.id);

    const seed = (composeTab.widgetData as { compose?: Record<string, unknown>; filename?: string });
    expect(seed.compose).toMatchObject({
      speDriveItemId: 'drive-item-1',
      sprkDocumentId: 'doc-1',
      speDriveId: 'drive-1',
      fileName: 'Brief.docx',
    });
    // R3 server-readable filename contract — hoisted to the top level.
    expect(seed.filename).toBe('Brief.docx');
  });

  // -------------------------------------------------------------------------
  // Contract 2 — relaunch reuses + activates the restored compose tab (#572 1d)
  // -------------------------------------------------------------------------

  it('activates the ALREADY-OPEN compose tab on relaunch (does not strand the user; no duplicate)', async () => {
    // Persisted session: a compose tab is open but Daily Briefing is the
    // persisted active tab — NFR-09 restore honors that activeTabId.
    restoreSnapshot = {
      tabs: [
        {
          id: 'wstab-1-workspace',
          widgetType: 'workspace',
          widgetData: { layoutId: 'layout-default', layoutName: 'Daily Briefing' },
          displayName: 'Daily Briefing',
        },
        {
          id: 'wstab-2-compose',
          widgetType: 'compose',
          widgetData: { compose: { speDriveItemId: 'drive-item-1', fileName: 'Brief.docx' } },
          displayName: 'Compose',
        },
      ],
      activeTabId: 'wstab-1-workspace',
    };

    renderPane();

    // The restored compose tab ends up ACTIVE — pre-fix (issue #572 Defect 1d)
    // the user was stranded on Daily Briefing with the tab strip hidden.
    await waitFor(
      () => {
        expect(recordedPatches.length).toBeGreaterThan(0);
        expect(recordedPatches[recordedPatches.length - 1]?.activeTabId).toBe(
          'wstab-2-compose',
        );
      },
      { timeout: 3000 },
    );

    // Compose-launch mode hides the tab strip.
    expect(screen.queryAllByRole('tab')).toHaveLength(0);

    // Every write-through keeps exactly ONE compose tab and TWO tabs total
    // (no re-install stacked a duplicate).
    for (const patch of recordedPatches) {
      expect(composeTabsIn(patch)).toHaveLength(1);
      expect(patch.tabs).toHaveLength(2);
    }
  });
});
