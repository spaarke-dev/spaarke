/**
 * WorkspacePane — DEF-08 single-tab reuse for compose-draft opens.
 *
 * Defect (DEF-08 side effect): every chat "open as a document" / "Open in Compose" widget_load
 * minted a NEW workspace tab, so repeated opens accumulated duplicate (often blank) Compose tabs.
 *
 * Fix under test: the `workspace.widget_load` handler REUSES the single existing Compose layout
 * tab (matched by widgetData.layoutId) for a compose-seeded open — it updates the seed + activates
 * the tab instead of adding a duplicate.
 *
 * Harness derived from WorkspacePane.compose-relaunch.test.tsx. Auto-install is disabled by
 * returning a null activeLayout (layoutForAutoInstall stays null → the auto-install effect
 * early-returns), so the ONLY tabs come from the widget_load events this test dispatches.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, waitFor } from '@testing-library/react';
import { act } from 'react-dom/test-utils';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

interface RecordedPatch {
  tabs: Array<{ id: string; widgetType: string }>;
  activeTabId: string | null;
}
const recordedPatches: RecordedPatch[] = [];

const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
  const method = init?.method ?? 'GET';
  if (method === 'GET' && url.includes('/tabs')) {
    return { ok: true, status: 200, json: async () => ({ tabs: [], activeTabId: null }) } as Partial<Response> as Response;
  }
  if (method === 'PATCH' && url.includes('/tabs')) {
    recordedPatches.push(JSON.parse(String(init?.body)) as RecordedPatch);
    return { ok: true, status: 204, json: async () => ({}) } as Partial<Response> as Response;
  }
  return { ok: false, status: 404, json: async () => ({}) } as Partial<Response> as Response;
});

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  const makeStub = (widgetType: string): React.FC<{ data?: unknown }> =>
    function StubWidget({ data }: { data?: unknown }): React.JSX.Element {
      const layoutName = (data as { layoutName?: string } | null)?.layoutName ?? widgetType;
      return <div data-testid="active-widget-stub">{layoutName}</div>;
    };
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn().mockResolvedValue('test-token'),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: 'session-reuse',
      setChatSessionId: jest.fn(),
      playbookId: undefined,
      setPlaybookId: jest.fn(),
      entityContext: null,
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

// No compose-launch mode + null activeLayout → auto-install effect early-returns (no default tab).
jest.mock('../../shell/ThreePaneShell', () => ({
  usePaneCollapseContext: () => null,
  useComposeLaunch: () => null,
}));

jest.mock('../../../hooks/useWorkspaceLayouts', () => ({
  useWorkspaceLayouts: () => ({
    layouts: [{ id: 'layout-compose', name: 'Compose', isSystem: true }],
    activeLayout: null,
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

// eslint-disable-next-line import/first
import { WorkspacePane } from '../WorkspacePane';

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

function composeDraftOpen(ledgerRef: string) {
  return {
    type: 'widget_load' as const,
    widgetType: 'workspace',
    widgetData: {
      layoutId: 'layout-compose',
      layoutName: 'Compose',
      compose: { draft: { ledgerRef, sessionId: 'session-reuse' } },
    },
    displayName: 'Compose',
  };
}

beforeEach(() => {
  recordedPatches.length = 0;
  authenticatedFetchMock.mockClear();
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

describe('WorkspacePane — DEF-08 compose-draft single-tab reuse', () => {
  it('a SECOND compose-draft open REUSES the single Compose tab (no duplicate)', async () => {
    const { bus } = renderPane();

    // First open — adds the Compose tab.
    await act(async () => {
      bus.dispatch('workspace', composeDraftOpen('binding-1@t1'));
    });
    await waitFor(() => {
      const composeTabPatches = recordedPatches.filter((p) =>
        p.tabs.some((t) => t.widgetType === 'workspace'),
      );
      expect(composeTabPatches.length).toBeGreaterThan(0);
    });

    // Second open (a fresh draft) — must REUSE, not duplicate.
    await act(async () => {
      bus.dispatch('workspace', composeDraftOpen('binding-1@t2'));
    });

    // Every PATCH that carries a workspace tab carries EXACTLY ONE (no accumulated duplicates).
    await waitFor(() => {
      const lastPatch = recordedPatches[recordedPatches.length - 1];
      expect(lastPatch).toBeDefined();
    });
    for (const patch of recordedPatches) {
      const workspaceTabs = patch.tabs.filter((t) => t.widgetType === 'workspace');
      expect(workspaceTabs.length).toBeLessThanOrEqual(1);
    }
    const finalWorkspaceTabs = recordedPatches[recordedPatches.length - 1].tabs.filter(
      (t) => t.widgetType === 'workspace',
    );
    expect(finalWorkspaceTabs).toHaveLength(1);
  });
});
