/**
 * WorkspacePane — "Compose" LAYOUT selection → DIRECT 'compose' re-route
 * (spaarkeai-compose-r2 UNIFY).
 *
 * The WorkspacePaneMenu "Compose" menu selection dispatches a workspace-LAYOUT
 * load (widgetType 'workspace' + layoutName 'Compose'). Before the flip, that
 * mounted the "Compose" LAYOUT tab (widgetType 'workspace'), which the round-7
 * keep-mounted-hidden keep-alive did NOT cover — opening the Email tab
 * auto-activated it and UNMOUNTED the layout Compose tab, destroying the loaded
 * document.
 *
 * Fix under test: the workspace handler RE-ROUTES a "Compose" layout load to
 * the DIRECT 'compose' widget (widgetType 'compose'), so it is covered by the
 * keep-alive. The plain menu selection carries no document → empty Compose
 * editor. Other layouts (Daily Briefing, dashboards) keep the 'workspace' door.
 *
 * Contracts asserted:
 *   1. A "Compose" workspace-LAYOUT load persists as a widgetType 'compose' tab
 *      (NOT 'workspace').
 *   2. A NON-Compose layout load (Daily Briefing) still persists as a
 *      widgetType 'workspace' tab (unaffected).
 *
 * Harness derived from WorkspacePane.compose-draft-reuse.test.tsx (normal mode:
 * useComposeLaunch → null, null activeLayout so nothing auto-installs; the only
 * tabs come from the widget_load events this test dispatches).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, waitFor } from '@testing-library/react';
import { act } from 'react-dom/test-utils';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

interface RecordedPatch {
  tabs: Array<{ id: string; widgetType: string; displayName: string }>;
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
    function StubWidget(): React.JSX.Element {
      return <div data-testid={`active-widget-${widgetType}`}>{widgetType}</div>;
    };
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn().mockResolvedValue('test-token'),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: 'session-reroute',
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

// Normal mode (NOT compose-launch) + null activeLayout → nothing auto-installs.
jest.mock('../../shell/ThreePaneShell', () => ({
  usePaneCollapseContext: () => null,
  useComposeLaunch: () => null,
}));

jest.mock('../../../hooks/useWorkspaceLayouts', () => ({
  useWorkspaceLayouts: () => ({
    layouts: [
      { id: 'layout-compose', name: 'Compose', isSystem: true },
      { id: 'layout-default', name: 'Daily Briefing', isSystem: true },
    ],
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

/** Exactly what WorkspacePaneMenu.handleLayoutSelect dispatches for a layout. */
function layoutSelect(layoutId: string, layoutName: string) {
  return {
    type: 'widget_load' as const,
    widgetType: 'workspace',
    widgetData: { layoutId, layoutName },
    displayName: layoutName,
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

describe('WorkspacePane — "Compose" layout selection re-routes to DIRECT compose', () => {
  it('a "Compose" workspace-LAYOUT selection persists as a widgetType:compose tab', async () => {
    const { bus } = renderPane();

    await act(async () => {
      bus.dispatch('workspace', layoutSelect('layout-compose', 'Compose'));
    });

    await waitFor(() => {
      const last = recordedPatches[recordedPatches.length - 1];
      expect(last).toBeDefined();
      expect(last.tabs.some(t => t.widgetType === 'compose')).toBe(true);
    });

    const last = recordedPatches[recordedPatches.length - 1];
    // Re-routed: it is a compose tab, NOT a workspace layout tab.
    expect(last.tabs.filter(t => t.widgetType === 'compose')).toHaveLength(1);
    expect(last.tabs.some(t => t.widgetType === 'workspace')).toBe(false);
  });

  it('a NON-Compose layout selection (Daily Briefing) still mounts as a widgetType:workspace tab', async () => {
    const { bus } = renderPane();

    await act(async () => {
      bus.dispatch('workspace', layoutSelect('layout-default', 'Daily Briefing'));
    });

    await waitFor(() => {
      const last = recordedPatches[recordedPatches.length - 1];
      expect(last).toBeDefined();
      expect(last.tabs.some(t => t.widgetType === 'workspace')).toBe(true);
    });

    const last = recordedPatches[recordedPatches.length - 1];
    expect(last.tabs.filter(t => t.widgetType === 'workspace')).toHaveLength(1);
    expect(last.tabs.some(t => t.widgetType === 'compose')).toBe(false);
  });
});
