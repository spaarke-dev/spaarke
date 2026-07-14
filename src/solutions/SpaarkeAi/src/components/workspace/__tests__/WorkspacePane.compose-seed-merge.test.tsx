/**
 * WorkspacePane.compose-seed-merge.test.tsx — UAT round-7 #1 (seed-merge fix).
 *
 * Bug: a re-activation of the single Compose tab that carries NO new `compose`
 * seed (e.g. an add-to-DMS `source`-only marker) OVERWROTE the tab's
 * `widgetData.compose` with a seedless object — so a later remount had nothing
 * to reload.
 *
 * Fix under test: the `workspace.widget_load` compose-reuse branch MERGES —
 * when the re-activation carries no `compose` seed it PRESERVES the existing
 * tab's `compose` seed instead of clobbering it. A genuine new seed still wins.
 *
 * Harness derived from WorkspacePane.compose-draft-reuse.test.tsx. Auto-install
 * is disabled via a null activeLayout so the only tabs come from dispatched
 * widget_load events. We inspect the persisted PATCH snapshot (which serializes
 * each tab's widgetData) to assert the compose seed survives the seedless
 * re-activation.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, waitFor } from '@testing-library/react';
import { act } from 'react-dom/test-utils';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

interface RecordedPatch {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  tabs: Array<{ id: string; widgetType: string; widgetData: any }>;
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
      chatSessionId: 'session-seed-merge',
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

const COMPOSE_SEED = { draft: { ledgerRef: 'lr-seed@t1', fileName: 'contract.docx', sessionId: 'session-seed-merge' } };

beforeEach(() => {
  recordedPatches.length = 0;
  authenticatedFetchMock.mockClear();
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

describe('WorkspacePane — compose seed-merge (UAT round-7 #1)', () => {
  it('a seedless re-activation PRESERVES the existing widgetData.compose seed', async () => {
    const { bus } = renderPane();

    // First open — a compose draft carrying the reloadable seed.
    await act(async () => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'compose',
        widgetData: { compose: COMPOSE_SEED },
        displayName: 'Compose',
      });
    });
    await waitFor(() => {
      const composeTabPatched = recordedPatches.some((p) =>
        p.tabs.some((t) => t.widgetType === 'compose' && t.widgetData?.compose),
      );
      expect(composeTabPatched).toBe(true);
    });

    // Seedless re-activation — carries only a `source` marker, NO compose seed.
    await act(async () => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'compose',
        widgetData: { source: 'dms' },
        displayName: 'Compose',
      });
    });

    // The post-reuse PATCH (identified by source==='dms') must STILL carry the
    // original compose seed — merge, not overwrite.
    await waitFor(() => {
      const last = recordedPatches[recordedPatches.length - 1];
      const composeTab = last?.tabs.find((t) => t.widgetType === 'compose');
      expect(composeTab?.widgetData?.source).toBe('dms');
    });

    const last = recordedPatches[recordedPatches.length - 1];
    const composeTab = last.tabs.find((t) => t.widgetType === 'compose');
    expect(composeTab?.widgetData?.compose).toEqual(COMPOSE_SEED);
    // filename hoisted from the original seed is preserved too.
    expect(composeTab?.widgetData?.filename).toBe('contract.docx');
  });
});
